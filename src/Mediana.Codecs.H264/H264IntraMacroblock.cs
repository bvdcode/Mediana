namespace Mediana.Codecs.H264
{
    internal static class H264IntraMacroblock
    {
        // Rec. ITU-T H.264, Table 9-4 (4:2:0 intra coded_block_pattern).
        private static ReadOnlySpan<byte> CodedPatterns =>
            [47, 31, 15, 0, 23, 27, 29, 30, 7, 11, 13, 14, 39, 43, 45, 46, 16, 3, 5, 10, 12, 19, 21, 26,
                28, 35, 37, 42, 44, 1, 2, 4, 8, 17, 18, 20, 24, 6, 9, 22, 25, 32, 33, 34, 36, 40, 38, 41];

        public static void Read(ref H264BitReader bits, H264Picture target, H264SliceHeader header, int address, int type, ref int qp)
        {
            int x = (address % header.Sequence.WidthInMbs) * 16;
            int y = (address / header.Sequence.WidthInMbs) * 16;
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
            H264IntraReconstruction.Read(ref bits, new H264CavlcResidualReader(target), target, header, x, y, type, chromaMode, pattern, qp);
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

    }
}
