// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264Residuals
    {
        public static void ReadInterLuma(ref H264BitReader bits, H264Picture target, int x, int y,
            int pattern, int qp, scoped ReadOnlySpan<byte> macroPrediction) =>
            ReadInterLuma(ref bits, new H264CavlcResidualReader(target), target, x, y, pattern, qp, macroPrediction);

        public static void ReadInterLuma<TReader>(ref H264BitReader bits, TReader reader, H264Picture target, int x, int y,
            int pattern, int qp, scoped ReadOnlySpan<byte> macroPrediction)
            where TReader : IH264ResidualReader
        {
            if (target.Transforms8x8[y / 16 * target.Sequence.WidthInMbs + x / 16] != 0)
            {
                H264Luma8x8Reconstruction.Read(ref bits, reader, target, x, y, pattern, qp, false, false, macroPrediction);
                return;
            }
            Span<int> coefficients = stackalloc int[16];
            Span<int> residual = stackalloc int[16];
            Span<byte> prediction = stackalloc byte[16];
            for (int index = 0; index < 16; index++)
            {
                (int blockX, int blockY) = H264Picture.LumaBlockPosition(index);
                coefficients.Clear();
                int count = (pattern & (1 << (index / 4))) != 0
                    ? reader.Read(ref bits, H264ResidualCategory.Luma4x4, 0, x + blockX, y + blockY, coefficients) : 0;
                target.Counts(0)[target.BlockIndex(0, x + blockX, y + blockY)] = (byte)count;
                H264InverseTransform.Residual4x4(coefficients, qp, residual);
                CopyPrediction(macroPrediction, 16, blockX, blockY, prediction);
                target.WriteBlock(0, x + blockX, y + blockY, prediction, residual);
            }
        }

        public static void ReadChroma(ref H264BitReader bits, H264Picture target, H264PictureParameterSet picture,
            int x, int y, int pattern, int qp, scoped ReadOnlySpan<byte> macroPrediction) =>
            ReadChroma(ref bits, new H264CavlcResidualReader(target), target, picture, x, y, pattern, qp, macroPrediction);

        public static void ReadChroma<TReader>(ref H264BitReader bits, TReader reader, H264Picture target, H264PictureParameterSet picture,
            int x, int y, int pattern, int qp, scoped ReadOnlySpan<byte> macroPrediction)
            where TReader : IH264ResidualReader
        {
            Span<int> coefficients = stackalloc int[15];
            Span<int> dcCoefficients = stackalloc int[8];
            Span<int> dc = stackalloc int[4];
            Span<int> residual = stackalloc int[16];
            Span<byte> prediction = stackalloc byte[16];
            dcCoefficients.Clear();
            if (pattern != 0)
            {
                reader.Read(ref bits, H264ResidualCategory.ChromaDc, 1, x, y, dcCoefficients[..4]);
                reader.Read(ref bits, H264ResidualCategory.ChromaDc, 2, x, y, dcCoefficients[4..]);
            }
            for (int plane = 1; plane <= 2; plane++)
            {
                int offset = plane == 1 ? picture.ChromaQpOffset : picture.SecondChromaQpOffset;
                int chromaQp = H264InverseTransform.ChromaQp(qp, offset);
                H264InverseTransform.ChromaDc(dcCoefficients.Slice((plane - 1) * 4, 4), chromaQp, dc);
                for (int index = 0; index < 4; index++)
                {
                    int blockX = (index % 2) * 4;
                    int blockY = (index / 2) * 4;
                    coefficients.Clear();
                    int count = pattern == 2
                        ? reader.Read(ref bits, H264ResidualCategory.ChromaAc, plane, x + blockX, y + blockY, coefficients) : 0;
                    target.Counts(plane)[target.BlockIndex(plane, x + blockX, y + blockY)] = (byte)count;
                    H264InverseTransform.Residual4x4(coefficients, chromaQp, residual, dc[index]);
                    CopyPrediction(macroPrediction.Slice((plane - 1) * 64, 64), 8, blockX, blockY, prediction);
                    target.WriteBlock(plane, x + blockX, y + blockY, prediction, residual);
                }
            }
        }

        public static void CopyPrediction(ReadOnlySpan<byte> source, int stride, int x, int y, Span<byte> block)
        {
            for (int row = 0; row < 4; row++)
            {
                source.Slice((y + row) * stride + x, 4).CopyTo(block.Slice(row * 4, 4));
            }
        }
    }
}
