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
            Span<byte> prediction = intra ? stackalloc byte[64 * target.SampleBytes] : [];
            for (int index = 0; index < 4; index++)
            {
                int blockX = x + index % 2 * 8;
                int blockY = y + index / 2 * 8;
                int count = (pattern & (1 << index)) != 0
                    ? reader.Read(ref bits, H264ResidualCategory.Luma8x8, plane, blockX, blockY, coefficients) : 0;
                scoped ReadOnlySpan<byte> predicted;
                int predictionStride;
                if (intra)
                {
                    int mode = target.Modes[target.BlockIndex(0, blockX, blockY)] & 127;
                    if (mode <= 2 && (count == 0 || (count == 1 && coefficients[0] != 0)))
                    {
                        int correction = count == 0 ? 0 : H264InverseTransform8x8.DcResidual(coefficients[0], qp);
                        target.ReconstructSimpleIntra8x8(plane, blockX, blockY, mode, constrained, correction, count == 0);
                        continue;
                    }
                    target.PredictLuma8x8(blockX, blockY, mode, prediction, constrained, plane);
                    predicted = prediction;
                    predictionStride = 8;
                }
                else
                {
                    predicted = macroPrediction[(((blockY - y) * 16 + blockX - x) * target.SampleBytes)..];
                    predictionStride = 16;
                }
                if (count == 0)
                {
                    target.WriteZeroResidualBlock(plane, blockX, blockY, 8, predicted, predictionStride);
                    continue;
                }
                if (count == 1 && coefficients[0] != 0)
                {
                    int delta = H264InverseTransform8x8.DcResidual(coefficients[0], qp);
                    target.WriteConstant8x8(plane, blockX, blockY, predicted, predictionStride, delta);
                    continue;
                }
                target.ReconstructLuma8x8(plane, blockX, blockY, predicted, predictionStride, coefficients, qp, field);
            }
        }
    }
}
