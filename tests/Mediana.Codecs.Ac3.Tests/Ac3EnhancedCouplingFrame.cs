// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Ac3.Tests
{
    internal static class Ac3EnhancedCouplingFrame
    {
        public static byte[] CreateSequence(bool merged)
        {
            List<byte> result = [];
            foreach (int begin in new[] { 0, 1, 2, 6, 13, 15 }) { result.AddRange(Create(begin, merged)); }
            return result.ToArray();
        }

        public static byte[] Create(int begin = 0, bool merged = false, bool spectral = false)
        {
            ReadOnlySpan<int> starts = [13, 19, 25, 31, 37, 49, 61, 73, 85, 97, 109, 121, 133, 145, 157, 169, 181, 193, 205, 217, 229, 241, 253];
            ReadOnlySpan<byte> defaultMerge = [0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 1, 0, 1, 1, 1, 0, 1, 1, 1];
            int first = begin < 3 ? begin * 2 : begin < 13 ? begin + 2 : begin * 2 - 10;
            int finish = spectral ? 5 : 22, bands = 1;
            for (int band = first + 1; band < finish; band++)
            { if (band < 9 || !(merged ? (band & 1) == 1 : defaultMerge[band] != 0)) { bands++; } }
            Ac3TestBitWriter writer = new(4096);
            writer.Write(0xb77, 16); writer.Write(0, 2); writer.Write(0, 3); writer.Write(2047, 11);
            writer.Write(0, 2); writer.Write(2, 2); writer.Write(2, 3); writer.Write(0, 1); writer.Write(16, 5);
            writer.Write(31, 5); writer.Write(0, 5);
            writer.Write(0, 2); writer.Write(0, 1);
            writer.Write(0, 1); writer.Write(1, 1); writer.Write(0, 5);
            writer.Write(1, 1); writer.Write(0, 2);
            for (int block = 0; block < 3; block++)
            { for (int channel = 0; channel < 3; channel++) { writer.Write(block == 0 ? 1 : 0, 2); } }
            writer.Write(0, 1); writer.Write(63, 6); writer.Write(15, 4); writer.Write(0, 1);
            for (int block = 0; block < 3; block++)
            {
                writer.Write(0, 3);
                if (spectral)
                {
                    if (block == 0) { Ac3SpectralFrame.WriteStrategy(writer, false, true); }
                    else { writer.Write(0, 1); }
                    for (int channel = 0; channel < 2; channel++)
                    {
                        if (block > 0) { writer.Write(0, 1); continue; }
                        writer.Write(31, 5); writer.Write(0, 2);
                        for (int band = 0; band < 3; band++) { writer.Write(60, 6); }
                    }
                }
                else { writer.Write(0, 1); }
                if (block == 0)
                {
                    writer.Write(1, 1); writer.Write(begin, 4);
                    if (!spectral) { writer.Write(15, 4); }
                    writer.Write(merged ? 1 : 0, 1);
                    if (merged)
                    { for (int band = Math.Max(9, first + 1); band < finish; band++) { writer.Write(band & 1, 1); } }
                }
                writer.Write(1, 1);
                for (int channel = 0; channel < 2; channel++)
                {
                    bool refresh = block == 0 || block == 2;
                    if (block > 0) { writer.Write(refresh ? 1 : 0, 1); if (channel == 1) { writer.Write(0, 1); } }
                    if (refresh)
                    { for (int band = 0; band < bands; band++) { writer.Write((band * 3 + channel * 13 + block * 4) % 32, 5); } }
                    if (block == 0 && channel == 1)
                    { for (int band = 1; band < bands; band++) { writer.Write(0x155, 9); } }
                    if (channel == 1) { writer.Write(1, 1); }
                }
                int rematrixBands = begin switch { 0 => 0, 1 => 1, 2 => 2, 3 or 4 => 3, _ => 4 };
                if (block == 0) { writer.Write((1 << rematrixBands) - 1, rematrixBands); }
                else { writer.Write(0, 1); }
                if (block == 0)
                {
                    WriteExponents(writer, (starts[finish] - starts[first]) / 3, false);
                    for (int channel = 0; channel < 2; channel++) { WriteExponents(writer, (starts[first] - 1) / 3, true); }
                }
                writer.Write(0, 1); writer.Write(0, block == 0 ? 6 : 1);
                for (int channel = 0; channel < 2; channel++)
                {
                    for (int bin = 0; bin < starts[first]; bin++) { writer.Write(Coefficient(bin, block, channel), 16); }
                    if (channel == 0)
                    { for (int bin = starts[first]; bin < starts[finish]; bin++) { writer.Write(Coefficient(bin, block, 2), 16); } }
                }
            }
            return writer.Finish(true);
        }

        private static int Coefficient(int bin, int block, int channel) => (bin * 17 + block * 23 + channel * 11) % 257 - 128;
        private static void WriteExponents(Ac3TestBitWriter writer, int groups, bool gainRange)
        {
            writer.Write(0, 4);
            for (int group = 0; group < groups; group++) { writer.Write(62, 7); }
            if (gainRange) { writer.Write(0, 2); }
        }
    }
}
