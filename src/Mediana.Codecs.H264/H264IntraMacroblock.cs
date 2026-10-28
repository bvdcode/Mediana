namespace Mediana.Codecs.H264
{
    internal static class H264IntraMacroblock
    {
        // Rec. ITU-T H.264, Table 9-4 (4:2:0 intra coded_block_pattern).
        private static ReadOnlySpan<byte> CodedPatterns =>
            [47, 31, 15, 0, 23, 27, 29, 30, 7, 11, 13, 14, 39, 43, 45, 46, 16, 3, 5, 10, 12, 19, 21, 26,
                28, 35, 37, 42, 44, 1, 2, 4, 8, 17, 18, 20, 24, 6, 9, 22, 25, 32, 33, 34, 36, 40, 38, 41];

        public static void Read(ref H264BitReader bits, H264Picture target, H264SliceHeader header, int address, ref int qp)
        {
            int x = (address % header.Sequence.WidthInMbs) * 16;
            int y = (address / header.Sequence.WidthInMbs) * 16;
            int type = bits.ReadUe(25);
            if (type == 25)
            {
                ReadPcm(ref bits, target, x, y);
                target.Quantizers[address] = 0;
                return;
            }
            bool intra16 = type != 0;
            for (int index = 0; index < 16; index++)
            {
                (int blockX, int blockY) = H264Picture.LumaBlockPosition(index);
                int mode = 2;
                if (!intra16)
                {
                    int predicted = target.PredictedMode(x + blockX, y + blockY, header.Picture.ConstrainedIntraPrediction);
                    mode = predicted;
                    if (!bits.ReadFlag())
                    {
                        int remaining = (int)bits.ReadBits(3);
                        mode = remaining < predicted ? remaining : remaining + 1;
                    }
                }
                target.SetLumaMode(x + blockX, y + blockY, mode);
            }
            int chromaMode = bits.ReadUe(3);
            int pattern = intra16 ? (((type - 1) / 12) * 15) + ((((type - 1) / 4) % 3) << 4)
                : CodedPatterns[bits.ReadUe(47)];
            if (intra16 || pattern != 0)
            {
                qp = (qp + bits.ReadSe(-26, 25) + 52) % 52;
            }
            target.Quantizers[address] = (byte)qp;
            ReadLuma(ref bits, target, x, y, type, pattern, qp, header.Picture.ConstrainedIntraPrediction);
            ReadChroma(ref bits, target, header.Picture, x / 2, y / 2, chromaMode, pattern >> 4, qp);
        }

        private static void ReadLuma(ref H264BitReader bits, H264Picture target, int x, int y, int type, int pattern, int qp, bool constrained)
        {
            bool intra16 = type != 0;
            Span<int> coefficients = stackalloc int[16];
            Span<int> dc = stackalloc int[16];
            Span<byte> macroPrediction = stackalloc byte[256];
            Span<byte> prediction = stackalloc byte[16];
            Span<int> residual = stackalloc int[16];
            if (intra16)
            {
                H264Cavlc.Read(ref bits, coefficients, target.NeighbourCount(0, x, y), 15);
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
                    count = H264Cavlc.Read(ref bits, block, target.NeighbourCount(0, x + blockX, y + blockY), 15);
                }
                target.Counts(0)[position] = (byte)count;
                if (intra16)
                {
                    CopyPrediction(macroPrediction, 16, blockX, blockY, prediction);
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

        private static void ReadChroma(ref H264BitReader bits, H264Picture target, H264PictureParameterSet picture,
            int x, int y, int mode, int pattern, int qp)
        {
            Span<int> coefficients = stackalloc int[15];
            Span<int> dcCoefficients = stackalloc int[8];
            Span<int> dc = stackalloc int[4];
            Span<int> residual = stackalloc int[16];
            Span<byte> macroPrediction = stackalloc byte[64];
            Span<byte> prediction = stackalloc byte[16];
            dcCoefficients.Clear();
            if (pattern != 0)
            {
                H264Cavlc.Read(ref bits, dcCoefficients[..4], -1, 15);
                H264Cavlc.Read(ref bits, dcCoefficients[4..], -1, 15);
            }
            for (int plane = 1; plane <= 2; plane++)
            {
                int offset = plane == 1 ? picture.ChromaQpOffset : picture.SecondChromaQpOffset;
                int chromaQp = H264InverseTransform.ChromaQp(qp, offset);
                H264InverseTransform.ChromaDc(dcCoefficients.Slice((plane - 1) * 4, 4), chromaQp, dc);
                target.PredictMacroblock(plane, x, y, mode, macroPrediction, picture.ConstrainedIntraPrediction);
                for (int index = 0; index < 4; index++)
                {
                    int blockX = (index % 2) * 4;
                    int blockY = (index / 2) * 4;
                    coefficients.Clear();
                    int count = pattern == 2
                        ? H264Cavlc.Read(ref bits, coefficients, target.NeighbourCount(plane, x + blockX, y + blockY), 15) : 0;
                    target.Counts(plane)[target.BlockIndex(plane, x + blockX, y + blockY)] = (byte)count;
                    H264InverseTransform.Residual4x4(coefficients, chromaQp, residual, dc[index]);
                    CopyPrediction(macroPrediction, 8, blockX, blockY, prediction);
                    target.WriteBlock(plane, x + blockX, y + blockY, prediction, residual);
                }
            }
        }

        private static void ReadPcm(ref H264BitReader bits, H264Picture target, int x, int y)
        {
            bits.AlignZero();
            for (int plane = 0; plane < 3; plane++)
            {
                int size = plane == 0 ? 16 : 8;
                int planeX = plane == 0 ? x : x / 2;
                int planeY = plane == 0 ? y : y / 2;
                int stride = target.Stride(plane);
                Span<byte> pixels = target.Plane(plane);
                for (int row = 0; row < size; row++)
                {
                    for (int column = 0; column < size; column++)
                    {
                        pixels[(planeY + row) * stride + planeX + column] = (byte)bits.ReadBits(8);
                    }
                }
                for (int row = 0; row < size; row += 4)
                {
                    for (int column = 0; column < size; column += 4)
                    {
                        int position = target.BlockIndex(plane, planeX + column, planeY + row);
                        target.Counts(plane)[position] = 16;
                        if (plane == 0)
                        {
                            target.Modes[position] = 2;
                        }
                    }
                }
            }
        }

        private static void CopyPrediction(ReadOnlySpan<byte> source, int stride, int x, int y, Span<byte> block)
        {
            for (int row = 0; row < 4; row++)
            {
                source.Slice((y + row) * stride + x, 4).CopyTo(block.Slice(row * 4, 4));
            }
        }
    }
}
