// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264Luma8x8Reconstruction
    {
        public static void Read<TReader>(ref H264BitReader bits, TReader reader, H264Picture target,
            int x, int y, int pattern, int qp, bool intra, bool constrained, scoped ReadOnlySpan<byte> macroPrediction)
            where TReader : IH264ResidualReader
        {
            H264InverseTransform.ValidateQp(qp);
            Span<int> coefficients = stackalloc int[64];
            Span<int> residual = stackalloc int[64];
            Span<byte> prediction = stackalloc byte[64];
            for (int index = 0; index < 4; index++)
            {
                int blockX = x + index % 2 * 8;
                int blockY = y + index / 2 * 8;
                coefficients.Clear();
                int count = (pattern & (1 << index)) != 0
                    ? reader.Read(ref bits, H264ResidualCategory.Luma8x8, 0, blockX, blockY, coefficients) : 0;
                if (intra)
                {
                    target.PredictLuma8x8(blockX, blockY, target.Modes[target.BlockIndex(0, blockX, blockY)] & 127, prediction, constrained);
                    if (count == 0)
                    {
                        target.WriteZeroResidualBlock(0, blockX, blockY, 8, prediction, 8);
                        continue;
                    }
                }
                else
                {
                    if (count == 0)
                    {
                        target.WriteZeroResidualBlock(0, blockX, blockY, 8, macroPrediction[((blockY - y) * 16 + blockX - x)..], 16);
                        continue;
                    }
                    for (int row = 0; row < 8; row++)
                    {
                        macroPrediction.Slice((blockY - y + row) * 16 + blockX - x, 8).CopyTo(prediction.Slice(row * 8, 8));
                    }
                }
                H264InverseTransform8x8.Residual(coefficients, qp, residual);
                target.WriteLuma8x8(blockX, blockY, prediction, residual);
            }
        }
    }
}
