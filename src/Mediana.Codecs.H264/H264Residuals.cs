// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264Residuals
    {
        public static void ReadInter<TReader>(ref H264BitReader bits, TReader reader, H264Picture target,
            H264PictureParameterSet picture, int x, int y, int pattern, int qp, scoped ReadOnlySpan<byte> prediction)
            where TReader : IH264ResidualReader
        {
            if (pattern == 0)
            {
                H264InverseTransform.ValidateQp(qp, target.Sequence.LumaBitDepth);
                target.WriteInterPrediction(x, y, prediction);
                return;
            }
            ReadInterLuma(ref bits, reader, target, x, y, pattern, qp, prediction[..target.LumaMacroblockBytes]);
            if (target.FullResolutionChroma)
            {
                for (int plane = 1; plane < 3; plane++)
                {
                    int offset = plane == 1 ? picture.ChromaQpOffset : picture.SecondChromaQpOffset;
                    int chromaQp = target.ChromaQuantizer.ChromaValue(qp - target.LumaQuantizer.Offset, offset);
                    ReadInterLuma(ref bits, reader, target, x, y, pattern, chromaQp,
                        prediction.Slice(plane * target.LumaMacroblockBytes, target.LumaMacroblockBytes), plane);
                }
                return;
            }
            ReadChroma(ref bits, reader, target, picture, x / target.ChromaHorizontalScale,
                y / target.ChromaVerticalScale, pattern >> 4, qp, prediction[target.LumaMacroblockBytes..]);
        }

        public static void ReadInterLuma(ref H264BitReader bits, H264Picture target, int x, int y,
            int pattern, int qp, scoped ReadOnlySpan<byte> macroPrediction) =>
            ReadInterLuma(ref bits, new H264CavlcResidualReader(target), target, x, y, pattern, qp, macroPrediction);

        public static void ReadInterLuma<TReader>(ref H264BitReader bits, TReader reader, H264Picture target, int x, int y,
            int pattern, int qp, scoped ReadOnlySpan<byte> macroPrediction, int plane = 0)
            where TReader : IH264ResidualReader
        {
            H264InverseTransform.ValidateQp(qp, target.Sequence.LumaBitDepth);
            if ((pattern & 15) == 0)
            {
                target.WriteZeroResidualBlock(plane, x, y, 16, macroPrediction, 16);
                return;
            }
            if (target.Transforms8x8[target.Grid.Address(x, y)] != 0)
            {
                H264Luma8x8Reconstruction.Read(ref bits, reader, target, x, y, pattern, qp, false, false, macroPrediction, plane);
                return;
            }
            bool field = target.IsFieldMacroblock(x, y);
            Span<int> coefficients = stackalloc int[16];
            Span<int> residual = stackalloc int[16];
            Span<byte> prediction = stackalloc byte[16 * target.SampleBytes];
            for (int group = 0; group < 4; group++)
            {
                int groupX = group % 2 * 8;
                int groupY = group / 2 * 8;
                if ((pattern & (1 << group)) == 0)
                {
                    target.WriteZeroResidualBlock(plane, x + groupX, y + groupY, 8,
                        macroPrediction[((groupY * 16 + groupX) * target.SampleBytes)..], 16);
                    continue;
                }
                for (int block = 0; block < 4; block++)
                {
                    int blockX = groupX + block % 2 * 4;
                    int blockY = groupY + block / 2 * 4;
                    coefficients.Clear();
                    int count = reader.Read(ref bits, H264ResidualCategory.Luma4x4, plane, x + blockX, y + blockY, coefficients);
                    if (count == 0)
                    {
                        target.WriteZeroResidualBlock(plane, x + blockX, y + blockY, 4,
                            macroPrediction[((blockY * 16 + blockX) * target.SampleBytes)..], 16);
                        continue;
                    }
                    target.Counts(plane)[target.BlockIndex(plane, x + blockX, y + blockY)] = (byte)count;
                    H264InverseTransform.Residual4x4(coefficients, qp, residual, bitDepth: target.Sequence.LumaBitDepth, field: field);
                    CopyPrediction(macroPrediction, 16, blockX, blockY, prediction, target.SampleBytes);
                    target.WriteBlock(plane, x + blockX, y + blockY, prediction, residual);
                }
            }
        }

        public static void ReadChroma(ref H264BitReader bits, H264Picture target, H264PictureParameterSet picture,
            int x, int y, int pattern, int qp, scoped ReadOnlySpan<byte> macroPrediction) =>
            ReadChroma(ref bits, new H264CavlcResidualReader(target), target, picture, x, y, pattern, qp, macroPrediction);

        public static void ReadChroma<TReader>(ref H264BitReader bits, TReader reader, H264Picture target, H264PictureParameterSet picture,
            int x, int y, int pattern, int qp, scoped ReadOnlySpan<byte> macroPrediction)
            where TReader : IH264ResidualReader
        {
            H264InverseTransform.ValidateQp(qp, target.Sequence.LumaBitDepth);
            int planeBytes = target.ChromaMacroblockBytes;
            int blocks = target.ChromaMacroblockSamples / 16;
            if (pattern == 0)
            {
                for (int plane = 1; plane <= 2; plane++)
                {
                    for (int row = 0; row < target.ChromaMacroblockHeight; row += 8)
                    {
                        target.WriteZeroResidualBlock(plane, x, y + row, 8,
                            macroPrediction.Slice((plane - 1) * planeBytes + row * 8 * target.SampleBytes, 64 * target.SampleBytes), 8);
                    }
                }
                return;
            }
            bool field = target.IsFieldMacroblock(x * 2, y * target.ChromaVerticalScale);
            Span<int> coefficients = stackalloc int[15];
            Span<int> dcCoefficients = stackalloc int[2 * blocks];
            Span<int> dc = stackalloc int[blocks];
            Span<int> residual = stackalloc int[16];
            Span<byte> prediction = stackalloc byte[16 * target.SampleBytes];
            dcCoefficients.Clear();
            reader.Read(ref bits, H264ResidualCategory.ChromaDc, 1, x, y, dcCoefficients[..blocks]);
            reader.Read(ref bits, H264ResidualCategory.ChromaDc, 2, x, y, dcCoefficients[blocks..]);
            for (int plane = 1; plane <= 2; plane++)
            {
                int offset = plane == 1 ? picture.ChromaQpOffset : picture.SecondChromaQpOffset;
                int chromaQp = target.ChromaQuantizer.ChromaValue(qp - target.LumaQuantizer.Offset, offset);
                H264InverseTransform.ChromaDc(dcCoefficients.Slice((plane - 1) * blocks, blocks), chromaQp, dc, target.Sequence.ChromaBitDepth);
                for (int index = 0; index < blocks; index++)
                {
                    int blockX = (index % 2) * 4;
                    int blockY = (index / 2) * 4;
                    coefficients.Clear();
                    int count = pattern == 2
                        ? reader.Read(ref bits, H264ResidualCategory.ChromaAc, plane, x + blockX, y + blockY, coefficients) : 0;
                    if (count == 0 && dc[index] == 0)
                    {
                        target.WriteZeroResidualBlock(plane, x + blockX, y + blockY, 4,
                            macroPrediction[((plane - 1) * planeBytes + (blockY * 8 + blockX) * target.SampleBytes)..], 8);
                        continue;
                    }
                    target.Counts(plane)[target.BlockIndex(plane, x + blockX, y + blockY)] = (byte)count;
                    H264InverseTransform.Residual4x4(coefficients, chromaQp, residual, dc[index], target.Sequence.ChromaBitDepth, field: field);
                    CopyPrediction(macroPrediction.Slice((plane - 1) * planeBytes, planeBytes), 8, blockX, blockY, prediction, target.SampleBytes);
                    target.WriteBlock(plane, x + blockX, y + blockY, prediction, residual);
                }
            }
        }

        public static void CopyPrediction(ReadOnlySpan<byte> source, int stride, int x, int y, Span<byte> block, int sampleBytes = 1)
        {
            for (int row = 0; row < 4; row++)
            {
                source.Slice(((y + row) * stride + x) * sampleBytes, 4 * sampleBytes)
                    .CopyTo(block.Slice(row * 4 * sampleBytes, 4 * sampleBytes));
            }
        }
    }
}
