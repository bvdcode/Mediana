// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.H264
{
    internal static class H264InterEncoder
    {
        public static MediaBuffer? Encode(VideoFrame input, H264Picture target, H264Picture reference, int frameNumber, int quantizer,
            MediaBufferAllocator allocator, H264EncoderOptions options, int maximumBytes, bool measureOverflow, out int encodedLength,
            CancellationToken token, H264MotionReference? interpolation = null)
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
            H264EncoderDeblocking.WriteHeader(bits, options.Deblocking);
            Span<byte> samples = stackalloc byte[H264SourceMacroblock.ByteCount];
            Span<byte> prediction = stackalloc byte[384];
            Span<int> coefficients = stackalloc int[256 + H264ChromaEncoder.CoefficientCount];
            int skipped = 0;
            int previousQuantizer = quantizer;
            for (int y = 0; y < target.Height; y += 16)
            {
                for (int x = 0; x < target.Width; x += 16)
                {
                    token.ThrowIfCancellationRequested();
                    H264SourceMacroblock source = new(input, x, y, samples);
                    (int motionX, int motionY, bool exact) = H264MotionSearch.Select(source, reference, target, x, y,
                        options.MotionSearchRange, prediction, token, interpolation);
                    (int skipX, int skipY) = H264MotionPrediction.Skip(target, x, y);
                    int predictedX = 0;
                    int predictedY = 0;
                    bool predicted = false;
                    int macroblockQuantizer = quantizer;
                    while (true)
                    {
                        int pattern = 0;
                        if (exact)
                        {
                            coefficients.Clear();
                        }
                        else
                        {
                            pattern = AnalyzeLuma(source.Luma, macroblockQuantizer, prediction[..256], coefficients[..256]);
                            pattern |= H264ChromaEncoder.Analyze(source.Chroma, macroblockQuantizer, prediction[256..], coefficients[256..]) << 4;
                        }
                        bool skip = pattern == 0 && motionX == skipX && motionY == skipY;
                        H264BitWriterCheckpoint start = bits.CreateCheckpoint();
                        H264BitWriterCheckpoint macroblock = default;
                        int effectiveQuantizer = previousQuantizer;
                        if (!skip)
                        {
                            if (!predicted)
                            {
                                (predictedX, predictedY) = H264MotionPrediction.Predict(target, H264ReferenceList.L0, x, y, 16, 16, 0, 0);
                                predicted = true;
                            }
                            bits.WriteUnsigned((uint)skipped);
                            macroblock = bits.CreateCheckpoint();
                            bits.WriteUnsigned(0);
                            bits.WriteSigned(motionX - predictedX);
                            bits.WriteSigned(motionY - predictedY);
                            bits.WriteUnsigned((uint)H264InterMacroblock.CodedPatterns.IndexOf((byte)pattern));
                            if (pattern != 0)
                            {
                                H264MacroblockEncoding.WriteQuantizerDelta(bits, previousQuantizer, macroblockQuantizer);
                                effectiveQuantizer = macroblockQuantizer;
                            }
                        }
                        target.SetMotion(H264ReferenceList.L0, x, y, 16, 16, 0, motionX, motionY);
                        target.Quantizers[y / 16 * target.Sequence.WidthInMbs + x / 16] = (byte)effectiveQuantizer;
                        bool written = TryWriteLuma(bits, target, x, y, macroblockQuantizer, pattern, prediction[..256], coefficients[..256]) &&
                            H264ChromaEncoder.TryWrite(bits, target, x / 2, y / 2, macroblockQuantizer, pattern >> 4, prediction[256..], coefficients[256..]);
                        if (written && (skip || H264MacroblockEncoding.Fits(bits, macroblock)))
                        {
                            skipped = skip ? skipped + 1 : 0;
                            previousQuantizer = effectiveQuantizer;
                            break;
                        }
                        if (written && H264PcmMacroblockEncoder.TryWrite(bits, macroblock, source, target, x, y, H264SliceKind.P))
                        {
                            skipped = 0;
                            break;
                        }
                        bits.Restore(start);
                        macroblockQuantizer = H264MacroblockEncoding.IncreaseQuantizer(macroblockQuantizer);
                        token.ThrowIfCancellationRequested();
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

        internal static int AnalyzeLuma(ReadOnlySpan<byte> source, int qp, ReadOnlySpan<byte> prediction, Span<int> coefficients)
        {
            int pattern = 0;
            for (int index = 0; index < 16; index += 2)
            {
                (int blockX, int blockY) = H264Picture.LumaBlockPosition(index);
                int offset = blockY * 16 + blockX;
                Span<int> levels = coefficients.Slice(index * 16, 32);
                H264ForwardTransform.QuantizePair(source[offset..], 16, prediction[offset..], 16, qp,
                    levels[..16], levels[16..]);
                if (levels.ContainsAnyExcept(0))
                {
                    pattern |= 1 << (index / 4);
                }
            }
            return pattern;
        }

        private static bool TryWriteLuma(H264BitWriter bits, H264Picture target, int x, int y, int qp, int pattern,
            ReadOnlySpan<byte> prediction, ReadOnlySpan<int> coefficients)
            => TryWriteLuma(new H264CavlcEncodingSyntax(bits, target), target, x, y, qp, pattern, prediction, coefficients);

        internal static bool TryWriteLuma<TSyntax>(TSyntax syntax, H264Picture target, int x, int y, int qp, int pattern,
            ReadOnlySpan<byte> prediction, ReadOnlySpan<int> coefficients) where TSyntax : IH264EncodingSyntax
        {
            if ((pattern & 15) == 0)
            {
                target.WriteZeroResidualBlock(0, x, y, 16, prediction, 16);
                return true;
            }
            Span<byte> block = stackalloc byte[16];
            Span<int> residual = stackalloc int[16];
            for (int index = 0; index < 16; index++)
            {
                (int blockX, int blockY) = H264Picture.LumaBlockPosition(index);
                ReadOnlySpan<int> levels = coefficients.Slice(index * 16, 16);
                int count = 0;
                if ((pattern & (1 << (index / 4))) != 0 &&
                    !syntax.TryWriteResidual(H264ResidualCategory.Luma4x4, 0, x + blockX, y + blockY, levels, out count))
                {
                    return false;
                }
                if (count == 0)
                {
                    target.WriteZeroResidualBlock(0, x + blockX, y + blockY, 4, prediction[(blockY * 16 + blockX)..], 16);
                    continue;
                }
                target.Counts(0)[target.BlockIndex(0, x + blockX, y + blockY)] = (byte)count;
                H264InverseTransform.Residual4x4(levels, qp, residual);
                H264Residuals.CopyPrediction(prediction, 16, blockX, blockY, block);
                target.WriteBlock(0, x + blockX, y + blockY, block, residual);
            }
            return true;
        }
    }
}
