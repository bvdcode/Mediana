// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.H264
{
    internal static class H264InterEncoder
    {
        public static MediaBuffer? Encode(VideoFrame input, H264Picture target, H264Picture reference, int frameNumber, int quantizer,
            MediaBufferAllocator allocator, H264EncoderOptions options, int maximumBytes, bool measureOverflow, out int encodedLength, CancellationToken token)
        {
            using H264BitWriter bits = new(allocator, maximumBytes, 0x41, lengthPrefixed: true, measureOverflow: true);
            bits.WriteUnsigned(0);
            bits.WriteUnsigned(5);
            bits.WriteUnsigned(0);
            bits.WriteBits((uint)frameNumber, 4);
            bits.WriteFlag(false);
            bits.WriteFlag(false);
            bits.WriteFlag(false);
            bits.WriteSigned(quantizer - options.Quantizer);
            bits.WriteUnsigned(1);
            Span<byte> prediction = stackalloc byte[384];
            Span<int> coefficients = stackalloc int[256 + H264ChromaEncoder.CoefficientCount];
            int skipped = 0;
            for (int y = 0; y < target.Height; y += 16)
            {
                for (int x = 0; x < target.Width; x += 16)
                {
                    token.ThrowIfCancellationRequested();
                    (int motionX, int motionY, bool exact) = H264MotionSearch.Select(input, reference, target, x, y,
                        options.MotionSearchRange, prediction, token);
                    int pattern = 0;
                    if (exact)
                    {
                        coefficients.Clear();
                    }
                    else
                    {
                        pattern = AnalyzeLuma(input, x, y, quantizer, prediction[..256], coefficients[..256]);
                        pattern |= H264ChromaEncoder.Analyze(input, x / 2, y / 2, quantizer, prediction[256..], coefficients[256..]) << 4;
                    }
                    (int skipX, int skipY) = H264MotionPrediction.Skip(target, x, y);
                    bool skip = pattern == 0 && motionX == skipX && motionY == skipY;
                    H264BitWriterCheckpoint checkpoint = default;
                    if (skip)
                    {
                        skipped++;
                    }
                    else
                    {
                        (int predictedX, int predictedY) = H264MotionPrediction.Predict(target, H264ReferenceList.L0, x, y, 16, 16, 0, 0);
                        bits.WriteUnsigned((uint)skipped);
                        skipped = 0;
                        checkpoint = bits.CreateCheckpoint();
                        bits.WriteUnsigned(0);
                        bits.WriteSigned(motionX - predictedX);
                        bits.WriteSigned(motionY - predictedY);
                        bits.WriteUnsigned((uint)H264InterMacroblock.CodedPatterns.IndexOf((byte)pattern));
                        if (pattern != 0)
                        {
                            bits.WriteSigned(0);
                        }
                    }
                    target.SetMotion(H264ReferenceList.L0, x, y, 16, 16, 0, motionX, motionY);
                    target.Quantizers[y / 16 * target.Sequence.WidthInMbs + x / 16] = (byte)quantizer;
                    WriteLuma(bits, target, x, y, quantizer, pattern, prediction[..256], coefficients[..256]);
                    H264ChromaEncoder.Write(bits, target, x / 2, y / 2, quantizer, pattern >> 4, prediction[256..], coefficients[256..]);
                    if (!skip)
                    {
                        H264PcmMacroblockEncoder.ReplaceOversized(bits, checkpoint, input, target, x, y, H264SliceKind.P);
                    }
                    if (!measureOverflow && bits.EncodedLength > maximumBytes)
                    {
                        encodedLength = bits.EncodedLength;
                        return null;
                    }
                }
            }
            token.ThrowIfCancellationRequested();
            if (skipped != 0)
            {
                bits.WriteUnsigned((uint)skipped);
            }
            MediaBuffer? result = bits.TryFinish();
            encodedLength = bits.EncodedLength;
            return result;
        }

        private static int AnalyzeLuma(VideoFrame input, int x, int y, int qp, ReadOnlySpan<byte> prediction, Span<int> coefficients)
        {
            Span<byte> block = stackalloc byte[16];
            Span<int> transformed = stackalloc int[16];
            int pattern = 0;
            for (int index = 0; index < 16; index++)
            {
                (int blockX, int blockY) = H264Picture.LumaBlockPosition(index);
                H264Residuals.CopyPrediction(prediction, 16, blockX, blockY, block);
                H264EncodingBlocks.Transform(input, 0, x + blockX, y + blockY, block, transformed);
                Span<int> levels = coefficients.Slice(index * 16, 16);
                H264ForwardTransform.Quantize4x4(transformed, qp, levels);
                if (levels.ContainsAnyExcept(0))
                {
                    pattern |= 1 << (index / 4);
                }
            }
            return pattern;
        }

        private static void WriteLuma(H264BitWriter bits, H264Picture target, int x, int y, int qp, int pattern,
            ReadOnlySpan<byte> prediction, ReadOnlySpan<int> coefficients)
        {
            Span<byte> block = stackalloc byte[16];
            Span<int> residual = stackalloc int[16];
            for (int index = 0; index < 16; index++)
            {
                (int blockX, int blockY) = H264Picture.LumaBlockPosition(index);
                ReadOnlySpan<int> levels = coefficients.Slice(index * 16, 16);
                int count = (pattern & (1 << (index / 4))) != 0
                    ? H264CavlcWriter.Write(bits, levels, target.NeighbourCount(0, x + blockX, y + blockY)) : 0;
                target.Counts(0)[target.BlockIndex(0, x + blockX, y + blockY)] = (byte)count;
                H264InverseTransform.Residual4x4(levels, qp, residual);
                H264Residuals.CopyPrediction(prediction, 16, blockX, blockY, block);
                target.WriteBlock(0, x + blockX, y + blockY, block, residual);
            }
        }
    }
}
