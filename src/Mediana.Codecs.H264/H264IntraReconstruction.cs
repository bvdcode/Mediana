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
            bool field = target.IsFieldMacroblock(x, y);
            bool intra16 = type != 0;
            if (!intra16 && target.Uses8x8Transform(target.Grid.Address(x, y)))
            {
                H264Luma8x8Reconstruction.Read(ref bits, reader, target, x, y, pattern, qp, true, constrained, [], plane);
                return;
            }
            Span<int> coefficients = stackalloc int[16];
            Span<int> dc = stackalloc int[16];
            Span<byte> macroPrediction = stackalloc byte[target.LumaMacroblockBytes];
            Span<byte> prediction = stackalloc byte[16 * target.SampleBytes];
            if (intra16)
            {
                reader.Read(ref bits, H264ResidualCategory.Intra16LumaDc, plane, x, y, coefficients);
                H264InverseTransform.LumaDc(coefficients, qp, dc, target.Sequence.LumaBitDepth, field: field);
                target.PredictMacroblock(plane, x, y, (type - 1) % 4, macroPrediction, constrained);
            }
            for (int index = 0; index < 16; index++)
            {
                (int blockX, int blockY) = H264Picture.LumaBlockPosition(index);
                int position = target.BlockIndex(plane, x + blockX, y + blockY);
                Span<int> block = intra16 ? coefficients[..15] : coefficients;
                int count = 0;
                block.Clear();
                if ((pattern & (1 << (index / 4))) != 0)
                {
                    count = reader.Read(ref bits, intra16 ? H264ResidualCategory.Intra16LumaAc : H264ResidualCategory.Luma4x4,
                        plane, x + blockX, y + blockY, block);
                }
                target.Counts(plane)[position] = (byte)count;
                if (intra16)
                {
                    if (count == 0 && dc[(blockY / 4) * 4 + blockX / 4] == 0)
                    {
                        target.WriteZeroResidualBlock(plane, x + blockX, y + blockY, 4, macroPrediction[((blockY * 16 + blockX) * target.SampleBytes)..], 16);
                        continue;
                    }
                    target.Reconstruct4x4(plane, x + blockX, y + blockY,
                        macroPrediction[((blockY * 16 + blockX) * target.SampleBytes)..], 16, block, qp,
                        dc[(blockY / 4) * 4 + blockX / 4], field);
                }
                else
                {
                    target.PredictLuma4x4(x + blockX, y + blockY, target.Modes[position] & 127, prediction, constrained, plane);
                    if (count == 0)
                    {
                        target.WriteZeroResidualBlock(plane, x + blockX, y + blockY, 4, prediction, 4);
                        continue;
                    }
                    target.Reconstruct4x4(plane, x + blockX, y + blockY, prediction, 4, block, qp, field: field);
                }
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
