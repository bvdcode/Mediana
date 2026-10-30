// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.H264
{
    internal static class H264IntraEncoder
    {
        public static MediaBuffer Encode(VideoFrame input, H264SequenceParameterSet sequence, int qp, int idrId,
            MediaBufferAllocator allocator, int maximumBytes, CancellationToken cancellationToken)
        {
            using H264Picture reconstructed = new(sequence, allocator, H264SliceKind.I);
            using H264BitWriter bits = new(allocator, maximumBytes, 0x65, lengthPrefixed: true);
            bits.WriteUnsigned(0);
            bits.WriteUnsigned(7);
            bits.WriteUnsigned(0);
            bits.WriteBits(0, 4);
            bits.WriteUnsigned((uint)idrId);
            bits.WriteFlag(false);
            bits.WriteFlag(false);
            bits.WriteSigned(0);
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
            return bits.Finish();
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
                Transform(input, 0, px, py, prediction, transformed);
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
            int chromaQp = H264InverseTransform.ChromaQp(qp, 0);
            Span<byte> prediction = stackalloc byte[128];
            Span<int> ac = stackalloc int[120];
            Span<int> dc = stackalloc int[8];
            Span<int> rawDc = stackalloc int[4];
            Span<int> transformed = stackalloc int[16];
            Span<byte> blockPrediction = stackalloc byte[16];
            for (int plane = 1; plane <= 2; plane++)
            {
                Span<byte> planePrediction = prediction.Slice((plane - 1) * 64, 64);
                reconstructed.PredictMacroblock(plane, x, y, 0, planePrediction);
                for (int index = 0; index < 4; index++)
                {
                    int blockX = index % 2 * 4;
                    int blockY = index / 2 * 4;
                    H264Residuals.CopyPrediction(planePrediction, 8, blockX, blockY, blockPrediction);
                    Transform(input, plane, x + blockX, y + blockY, blockPrediction, transformed);
                    rawDc[index] = transformed[0];
                    H264ForwardTransform.Quantize4x4(transformed, chromaQp, ac.Slice(((plane - 1) * 4 + index) * 15, 15));
                }
                Span<int> planeDc = dc.Slice((plane - 1) * 4, 4);
                H264ForwardTransform.ChromaDc(rawDc, chromaQp, planeDc);
                H264CavlcWriter.Write(bits, planeDc, -1);
            }
            Span<int> scaledDc = stackalloc int[4];
            Span<int> residual = stackalloc int[16];
            for (int plane = 1; plane <= 2; plane++)
            {
                H264InverseTransform.ChromaDc(dc.Slice((plane - 1) * 4, 4), chromaQp, scaledDc);
                for (int index = 0; index < 4; index++)
                {
                    int blockX = index % 2 * 4;
                    int blockY = index / 2 * 4;
                    Span<int> levels = ac.Slice(((plane - 1) * 4 + index) * 15, 15);
                    int count = H264CavlcWriter.Write(bits, levels, reconstructed.NeighbourCount(plane, x + blockX, y + blockY));
                    reconstructed.Counts(plane)[reconstructed.BlockIndex(plane, x + blockX, y + blockY)] = (byte)count;
                    H264Residuals.CopyPrediction(prediction.Slice((plane - 1) * 64, 64), 8, blockX, blockY, blockPrediction);
                    H264InverseTransform.Residual4x4(levels, chromaQp, residual, scaledDc[index]);
                    reconstructed.WriteBlock(plane, x + blockX, y + blockY, blockPrediction, residual);
                }
            }
        }

        private static void Transform(VideoFrame input, int plane, int x, int y, ReadOnlySpan<byte> prediction, Span<int> transformed)
        {
            Span<int> residual = stackalloc int[16];
            for (int row = 0; row < 4; row++)
            {
                ReadOnlySpan<byte> source = input.GetRow(plane, Math.Min(y + row, input.Planes[plane].Height - 1));
                for (int column = 0; column < 4; column++)
                {
                    residual[row * 4 + column] = source[Math.Min(x + column, source.Length - 1)] - prediction[row * 4 + column];
                }
            }
            H264ForwardTransform.Transform4x4(residual, transformed);
        }
    }
}
