namespace Mediana.Codecs.H264
{
    internal static class H264InterMacroblock
    {
        // Rec. ITU-T H.264, Table 9-4 (4:2:0 inter coded_block_pattern).
        private static ReadOnlySpan<byte> CodedPatterns =>
            [0, 16, 1, 2, 4, 8, 32, 3, 5, 10, 12, 15, 47, 7, 11, 13, 14, 6, 9, 31, 35, 37, 42, 44,
                33, 34, 36, 40, 39, 43, 45, 46, 17, 18, 20, 24, 19, 21, 26, 28, 23, 27, 29, 30, 22, 25, 38, 41];

        public static void Read(ref H264BitReader bits, H264Picture target, H264SliceHeader header,
            IReadOnlyList<H264ReferencePicture?> references, int address, ref int qp, bool skip = false)
        {
            int type = skip ? 0 : bits.ReadUe(30);
            if (type >= 5)
            {
                H264IntraMacroblock.Read(ref bits, target, header, address, type - 5, ref qp);
                return;
            }
            int x = (address % header.Sequence.WidthInMbs) * 16;
            int y = (address / header.Sequence.WidthInMbs) * 16;
            Span<byte> prediction = stackalloc byte[384];
            bool noSmallPartitions = true;
            if (skip)
            {
                H264InterPrediction.Skip(target, header, references, x, y, prediction);
            }
            else
            {
                noSmallPartitions = H264InterPrediction.Read(ref bits, new H264CavlcPredictionReader(), target, header, references, x, y, type, prediction);
            }
            int pattern = skip ? 0 : CodedPatterns[bits.ReadUe(47)];
            if ((pattern & 15) != 0 && header.Picture.Transform8x8 && noSmallPartitions && bits.ReadFlag())
            {
                target.Transforms8x8[address] = 1;
            }
            if (pattern != 0)
            {
                qp = (qp + bits.ReadSe(-26, 25) + 52) % 52;
            }
            target.Quantizers[address] = (byte)qp;
            H264Residuals.ReadInterLuma(ref bits, target, x, y, pattern, qp, prediction[..256]);
            H264Residuals.ReadChroma(ref bits, target, header.Picture, x / 2, y / 2, pattern >> 4, qp, prediction[256..]);
        }

    }
}
