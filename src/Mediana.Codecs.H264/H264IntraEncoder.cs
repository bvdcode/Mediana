// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.H264
{
    internal static class H264IntraEncoder
    {
        public static MediaBuffer? Encode(VideoFrame input, H264Picture reconstructed, int qp, int initialQuantizer, int idrId,
            MediaBufferAllocator allocator, int maximumBytes, bool measureOverflow, out int encodedLength, CancellationToken cancellationToken)
        {
            using H264BitWriter bits = new(allocator, maximumBytes, 0x65, lengthPrefixed: true, measureOverflow: measureOverflow);
            bits.WriteUnsigned(0);
            bits.WriteUnsigned(7);
            bits.WriteUnsigned(0);
            bits.WriteBits(0, 4);
            bits.WriteUnsigned((uint)idrId);
            bits.WriteFlag(false);
            bits.WriteFlag(false);
            bits.WriteSigned(qp - initialQuantizer);
            bits.WriteUnsigned(1);
            for (int y = 0; y < reconstructed.Height; y += 16)
            {
                for (int x = 0; x < reconstructed.Width; x += 16)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    WriteMacroblock(bits, input, reconstructed, x, y, qp);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            MediaBuffer? result = bits.TryFinish();
            encodedLength = bits.EncodedLength;
            return result;
        }

        private static void WriteMacroblock(H264BitWriter bits, VideoFrame input, H264Picture reconstructed, int x, int y, int qp)
        {
            bits.WriteUnsigned(0);
            for (int index = 0; index < 16; index++)
            {
                bits.WriteFlag(true);
            }
            bits.WriteUnsigned(0);
            bits.WriteUnsigned(0);
            bits.WriteSigned(0);
            Span<byte> prediction = stackalloc byte[16];
            Span<int> transformed = stackalloc int[16];
            Span<int> levels = stackalloc int[16];
            Span<int> residual = stackalloc int[16];
            for (int index = 0; index < 16; index++)
            {
                (int blockX, int blockY) = H264Picture.LumaBlockPosition(index);
                int px = x + blockX;
                int py = y + blockY;
                reconstructed.SetLumaMode(px, py, 2);
                reconstructed.PredictLuma4x4(px, py, 2, prediction);
                H264EncodingBlocks.Transform(input, 0, px, py, prediction, transformed);
                H264ForwardTransform.Quantize4x4(transformed, qp, levels);
                int count = H264CavlcWriter.Write(bits, levels, reconstructed.NeighbourCount(0, px, py));
                reconstructed.Counts(0)[reconstructed.BlockIndex(0, px, py)] = (byte)count;
                H264InverseTransform.Residual4x4(levels, qp, residual);
                reconstructed.WriteBlock(0, px, py, prediction, residual);
            }
            WriteChroma(bits, input, reconstructed, x / 2, y / 2, qp);
        }

        private static void WriteChroma(H264BitWriter bits, VideoFrame input, H264Picture reconstructed, int x, int y, int qp)
        {
            Span<byte> prediction = stackalloc byte[128];
            Span<int> coefficients = stackalloc int[H264ChromaEncoder.CoefficientCount];
            for (int plane = 1; plane <= 2; plane++)
            {
                reconstructed.PredictMacroblock(plane, x, y, 0, prediction.Slice((plane - 1) * 64, 64));
            }
            H264ChromaEncoder.Analyze(input, x, y, qp, prediction, coefficients);
            H264ChromaEncoder.Write(bits, reconstructed, x, y, qp, 2, prediction, coefficients);
        }
    }
}
