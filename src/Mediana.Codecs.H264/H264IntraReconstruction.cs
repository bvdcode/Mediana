// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264IntraReconstruction
    {
        public static void Read<TReader>(ref H264BitReader bits, TReader reader, H264Picture target,
            H264SliceHeader header, int x, int y, int type, int chromaMode, int pattern, int qp)
            where TReader : IH264ResidualReader
        {
            H264InverseTransform.ValidateQp(qp, target.Sequence.LumaBitDepth);
            ReadLuma(ref bits, reader, target, x, y, type, pattern, qp, header.Picture.ConstrainedIntraPrediction);
            if (target.FullResolutionChroma)
            {
                for (int plane = 1; plane < 3; plane++)
                {
                    for (int index = 0; index < 16; index++)
                    {
                        (int blockX, int blockY) = H264Picture.LumaBlockPosition(index);
                        target.Modes[target.BlockIndex(0, x + blockX, y + blockY)] |= 128;
                    }
                    int offset = plane == 1 ? header.Picture.ChromaQpOffset : header.Picture.SecondChromaQpOffset;
                    int chromaQp = target.ChromaQuantizer.ChromaValue(qp - target.LumaQuantizer.Offset, offset);
                    ReadLuma(ref bits, reader, target, x, y, type, pattern, chromaQp, header.Picture.ConstrainedIntraPrediction, plane);
                }
                return;
            }
            ReadChroma(ref bits, reader, target, header.Picture, x / 2, y / target.ChromaVerticalScale, chromaMode, pattern >> 4, qp);
        }

        private static void ReadLuma<TReader>(ref H264BitReader bits, TReader reader, H264Picture target, int x, int y, int type, int pattern, int qp, bool constrained, int plane = 0)
            where TReader : IH264ResidualReader
        {
            if (type != 0)
            {
                H264Intra16Reconstruction.Read(ref bits, reader, target, plane, x, y, (type - 1) % 4, pattern & 15, qp, constrained);
                return;
            }
            if (target.Uses8x8Transform(target.Grid.Address(x, y)))
            {
                H264Luma8x8Reconstruction.Read(ref bits, reader, target, x, y, pattern, qp, true, constrained, [], plane);
                return;
            }
            bool field = target.IsFieldMacroblock(x, y);
            Span<int> coefficients = stackalloc int[16];
            Span<byte> prediction = stackalloc byte[16 * target.SampleBytes];
            Span<byte> counts = target.Counts(plane);
            Span<byte> modes = target.Modes;
            for (int index = 0; index < 16; index++)
            {
                (int blockX, int blockY) = H264Picture.LumaBlockPosition(index);
                int column = x + blockX;
                int row = y + blockY;
                int position = target.BlockIndex(plane, column, row);
                int count = 0;
                if ((pattern & (1 << (index / 4))) != 0)
                {
                    coefficients.Clear();
                    count = reader.Read(ref bits, H264ResidualCategory.Luma4x4, plane, column, row, coefficients);
                }
                counts[position] = (byte)count;
                target.PredictLuma4x4(column, row, modes[position] & 127, prediction, constrained, plane);
                if (count == 0)
                {
                    target.WriteZeroResidualBlock(plane, column, row, 4, prediction, 4);
                    continue;
                }
                target.Reconstruct4x4(plane, column, row, prediction, 4, coefficients, qp, field: field);
            }
        }

        private static void ReadChroma<TReader>(ref H264BitReader bits, TReader reader, H264Picture target, H264PictureParameterSet picture,
            int x, int y, int mode, int pattern, int qp)
            where TReader : IH264ResidualReader
        {
            int planeBytes = target.ChromaMacroblockBytes;
            Span<byte> prediction = stackalloc byte[2 * planeBytes];
            for (int plane = 1; plane <= 2; plane++)
            {
                target.PredictMacroblock(plane, x, y, mode, prediction.Slice((plane - 1) * planeBytes, planeBytes), picture.ConstrainedIntraPrediction);
            }
            H264Residuals.ReadChroma(ref bits, reader, target, picture, x, y, pattern, qp, prediction);
        }

    }
}
