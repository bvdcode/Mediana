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
            ReadLuma(ref bits, reader, target, x, y, type, pattern, qp, header.Picture.ConstrainedIntraPrediction);
            ReadChroma(ref bits, reader, target, header.Picture, x / 2, y / 2, chromaMode, pattern >> 4, qp);
        }

        private static void ReadLuma<TReader>(ref H264BitReader bits, TReader reader, H264Picture target, int x, int y, int type, int pattern, int qp, bool constrained)
            where TReader : IH264ResidualReader
        {
            bool intra16 = type != 0;
            if (!intra16 && target.Transforms8x8[y / 16 * target.Sequence.WidthInMbs + x / 16] != 0)
            {
                H264Luma8x8Reconstruction.Read(ref bits, reader, target, x, y, pattern, qp, true, constrained, []);
                return;
            }
            Span<int> coefficients = stackalloc int[16];
            Span<int> dc = stackalloc int[16];
            Span<byte> macroPrediction = stackalloc byte[256];
            Span<byte> prediction = stackalloc byte[16];
            Span<int> residual = stackalloc int[16];
            if (intra16)
            {
                reader.Read(ref bits, H264ResidualCategory.Intra16LumaDc, 0, x, y, coefficients);
                H264InverseTransform.LumaDc(coefficients, qp, dc);
                target.PredictMacroblock(0, x, y, (type - 1) % 4, macroPrediction, constrained);
            }
            for (int index = 0; index < 16; index++)
            {
                (int blockX, int blockY) = H264Picture.LumaBlockPosition(index);
                int position = target.BlockIndex(0, x + blockX, y + blockY);
                Span<int> block = intra16 ? coefficients[..15] : coefficients;
                int count = 0;
                block.Clear();
                if ((pattern & (1 << (index / 4))) != 0)
                {
                    count = reader.Read(ref bits, intra16 ? H264ResidualCategory.Intra16LumaAc : H264ResidualCategory.Luma4x4,
                        0, x + blockX, y + blockY, block);
                }
                target.Counts(0)[position] = (byte)count;
                if (intra16)
                {
                    H264Residuals.CopyPrediction(macroPrediction, 16, blockX, blockY, prediction);
                    H264InverseTransform.Residual4x4(block, qp, residual, dc[(blockY / 4) * 4 + blockX / 4]);
                }
                else
                {
                    target.PredictLuma4x4(x + blockX, y + blockY, target.Modes[position] & 127, prediction, constrained);
                    H264InverseTransform.Residual4x4(block, qp, residual);
                }
                target.WriteBlock(0, x + blockX, y + blockY, prediction, residual);
            }
        }

        private static void ReadChroma<TReader>(ref H264BitReader bits, TReader reader, H264Picture target, H264PictureParameterSet picture,
            int x, int y, int mode, int pattern, int qp)
            where TReader : IH264ResidualReader
        {
            Span<byte> prediction = stackalloc byte[128];
            for (int plane = 1; plane <= 2; plane++)
            {
                target.PredictMacroblock(plane, x, y, mode, prediction.Slice((plane - 1) * 64, 64), picture.ConstrainedIntraPrediction);
            }
            H264Residuals.ReadChroma(ref bits, reader, target, picture, x, y, pattern, qp, prediction);
        }

    }
}
