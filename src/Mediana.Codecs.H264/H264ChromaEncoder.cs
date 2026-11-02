// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264ChromaEncoder
    {
        public const int CoefficientCount = 128;

        public static int Analyze(ReadOnlySpan<byte> source, int qp, ReadOnlySpan<byte> prediction, Span<int> coefficients)
        {
            int chromaQp = H264InverseTransform.ChromaQp(qp, 0);
            Span<int> rawDc = stackalloc int[4];
            Span<int> transformed = stackalloc int[16];
            for (int plane = 1; plane <= 2; plane++)
            {
                ReadOnlySpan<byte> planeSource = source.Slice((plane - 1) * 64, 64);
                ReadOnlySpan<byte> planePrediction = prediction.Slice((plane - 1) * 64, 64);
                for (int index = 0; index < 4; index++)
                {
                    int blockX = index % 2 * 4;
                    int blockY = index / 2 * 4;
                    int offset = blockY * 8 + blockX;
                    H264EncodingBlocks.Transform(planeSource[offset..], 8, planePrediction[offset..], 8, transformed);
                    rawDc[index] = transformed[0];
                    H264ForwardTransform.Quantize4x4(transformed, chromaQp, coefficients.Slice(((plane - 1) * 4 + index) * 15, 15));
                }
                H264ForwardTransform.ChromaDc(rawDc, chromaQp, coefficients.Slice(120 + (plane - 1) * 4, 4));
            }
            if (coefficients[..120].ContainsAnyExcept(0))
            {
                return 2;
            }
            return coefficients.Slice(120, 8).ContainsAnyExcept(0) ? 1 : 0;
        }

        public static bool TryWrite(H264BitWriter bits, H264Picture target, int x, int y, int qp, int pattern,
            ReadOnlySpan<byte> prediction, ReadOnlySpan<int> coefficients)
        {
            if (pattern == 0)
            {
                for (int plane = 1; plane <= 2; plane++)
                {
                    target.WriteZeroResidualBlock(plane, x, y, 8, prediction.Slice((plane - 1) * 64, 64), 8);
                }
                return true;
            }
            if (!H264CavlcWriter.TryWrite(bits, coefficients.Slice(120, 4), -1, out _) ||
                !H264CavlcWriter.TryWrite(bits, coefficients.Slice(124, 4), -1, out _))
            {
                return false;
            }
            int chromaQp = H264InverseTransform.ChromaQp(qp, 0);
            Span<int> scaledDc = stackalloc int[4];
            Span<int> residual = stackalloc int[16];
            Span<byte> blockPrediction = stackalloc byte[16];
            for (int plane = 1; plane <= 2; plane++)
            {
                H264InverseTransform.ChromaDc(coefficients.Slice(120 + (plane - 1) * 4, 4), chromaQp, scaledDc);
                for (int index = 0; index < 4; index++)
                {
                    int blockX = index % 2 * 4;
                    int blockY = index / 2 * 4;
                    ReadOnlySpan<int> levels = coefficients.Slice(((plane - 1) * 4 + index) * 15, 15);
                    int count = 0;
                    if (pattern == 2 && !H264CavlcWriter.TryWrite(bits, levels, target.NeighbourCount(plane, x + blockX, y + blockY), out count))
                    {
                        return false;
                    }
                    if (count == 0 && scaledDc[index] == 0)
                    {
                        target.WriteZeroResidualBlock(plane, x + blockX, y + blockY, 4,
                            prediction[((plane - 1) * 64 + blockY * 8 + blockX)..], 8);
                        continue;
                    }
                    target.Counts(plane)[target.BlockIndex(plane, x + blockX, y + blockY)] = (byte)count;
                    H264Residuals.CopyPrediction(prediction.Slice((plane - 1) * 64, 64), 8, blockX, blockY, blockPrediction);
                    H264InverseTransform.Residual4x4(levels, chromaQp, residual, scaledDc[index]);
                    target.WriteBlock(plane, x + blockX, y + blockY, blockPrediction, residual);
                }
            }
            return true;
        }
    }
}
