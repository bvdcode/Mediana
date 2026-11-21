// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264Luma8x8Reconstruction
    {
        public static void Read<TReader>(ref H264BitReader bits, TReader reader, H264Picture target,
            int x, int y, int pattern, int qp, bool intra, bool constrained, scoped ReadOnlySpan<byte> macroPrediction, int plane = 0)
            where TReader : IH264ResidualReader
        {
            H264InverseTransform.ValidateQp(qp, target.Sequence.LumaBitDepth);
            bool field = target.IsFieldMacroblock(x, y);
            Span<int> coefficients = stackalloc int[64];
            Span<int> residual = stackalloc int[64];
            Span<byte> prediction = stackalloc byte[64 * target.SampleBytes];
            for (int index = 0; index < 4; index++)
            {
                int blockX = x + index % 2 * 8;
                int blockY = y + index / 2 * 8;
                coefficients.Clear();
                int count = (pattern & (1 << index)) != 0
                    ? reader.Read(ref bits, H264ResidualCategory.Luma8x8, plane, blockX, blockY, coefficients) : 0;
                if (intra)
                {
                    target.PredictLuma8x8(blockX, blockY, target.Modes[target.BlockIndex(0, blockX, blockY)] & 127, prediction, constrained, plane);
                    if (count == 0)
                    {
                        target.WriteZeroResidualBlock(plane, blockX, blockY, 8, prediction, 8);
                        continue;
                    }
                }
                else
                {
                    if (count == 0)
                    {
                        target.WriteZeroResidualBlock(plane, blockX, blockY, 8, macroPrediction[(((blockY - y) * 16 + blockX - x) * target.SampleBytes)..], 16);
                        continue;
                    }
                    for (int row = 0; row < 8; row++)
                    {
                        macroPrediction.Slice(((blockY - y + row) * 16 + blockX - x) * target.SampleBytes, 8 * target.SampleBytes)
                            .CopyTo(prediction.Slice(row * 8 * target.SampleBytes, 8 * target.SampleBytes));
                    }
                }
                H264InverseTransform8x8.Residual(coefficients, qp, residual, target.Sequence.LumaBitDepth, field: field);
                target.WriteLuma8x8(blockX, blockY, prediction, residual, plane);
            }
        }
    }
}
