// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Ac3.Tests
{
    internal static class Ac3SpectralFrame
    {
        public static byte[] Create(int attenuation = -1, bool merged = false, int blend = 31, bool stereo = false, bool coupled = false)
        {
            int channels = stereo ? 2 : 1;
            Ac3TestBitWriter writer = new(2048);
            WriteHeader(writer, channels, coupled, attenuation);
            for (int block = 0; block < 6; block++)
            {
                writer.Write(0, channels + 1);
                if (block == 0) { WriteStrategy(writer, merged, stereo); }
                else { writer.Write(0, 1); }
                for (int channel = 0; channel < channels; channel++)
                {
                    if (block != 0) { writer.Write(0, 1); continue; }
                    writer.Write(blend, 5); writer.Write(0, 2);
                    for (int band = 0; band < (merged ? 2 : 3); band++) { writer.Write(0, 6); }
                }
                if (coupled)
                {
                    if (block == 0) { writer.Write(0, 7); }
                    for (int channel = 0; channel < channels; channel++) { writer.Write(0, block == 0 ? 10 : 1); }
                }
                if (stereo)
                {
                    if (block == 0) { writer.Write(coupled ? 3 : 7, coupled ? 2 : 3); }
                    else { writer.Write(0, 1); }
                }
                if (block == 0)
                {
                    if (coupled) { WriteExponents(writer, 4, false); }
                    for (int channel = 0; channel < channels; channel++) { WriteExponents(writer, coupled ? 12 : 16, true); }
                }
                writer.Write(0, 1);
                if (coupled) { writer.Write(0, block == 0 ? 6 : 1); }
                for (int channel = 0; channel < channels; channel++)
                {
                    for (int bin = 0; bin < (coupled ? 37 : 49); bin++)
                    { writer.Write((bin switch { 3 => 5, 19 => -3, 33 => 33, 39 => -17, 46 => 7, 47 => -11, 48 => 13, _ => 0 }) * (channel + 1), 16); }
                    if (channel == 0 && coupled)
                    { for (int bin = 37; bin < 49; bin++) { writer.Write(bin switch { 37 => 7, 39 => -3, 46 => 11, _ => 0 }, 16); } }
                }
            }
            return writer.Finish(true);
        }

        public static void WriteStrategy(Ac3TestBitWriter writer, bool merged, bool stereo = false)
        {
            writer.Write(1, 1);
            if (stereo) { writer.Write(3, 2); }
            writer.Write(0, 2); writer.Write(0, 3); writer.Write(0, 3);
            writer.Write(merged ? 1 : 0, 1);
            if (merged) { writer.Write(1, 1); writer.Write(0, 1); }
        }

        private static void WriteHeader(Ac3TestBitWriter writer, int channels, bool coupled, int attenuation)
        {
            writer.Write(0xb77, 16); writer.Write(0, 2); writer.Write(0, 3); writer.Write(1023, 11);
            writer.Write(0, 2); writer.Write(3, 2); writer.Write(channels, 3); writer.Write(0, 1); writer.Write(16, 5);
            writer.Write(31, 5); writer.Write(0, 4);
            writer.Write(1, 1); writer.Write(0, 1); writer.Write(0, 2); writer.Write(0, 1);
            writer.Write(0, 1); writer.Write(1, 1); writer.Write(0, 4); writer.Write(attenuation >= 0 ? 1 : 0, 1);
            if (channels == 2) { writer.Write(coupled ? 1 : 0, 1); writer.Write(0, 5); }
            for (int block = 0; block < 6; block++)
            {
                if (coupled) { writer.Write(block == 0 ? 1 : 0, 2); }
                for (int channel = 0; channel < channels; channel++) { writer.Write(block == 0 ? 1 : 0, 2); }
            }
            writer.Write(0, 5 * channels); writer.Write(63, 6); writer.Write(15, 4);
            if (attenuation >= 0)
            { for (int channel = 0; channel < channels; channel++) { writer.Write(1, 1); writer.Write(attenuation, 5); } }
            writer.Write(0, 1);
        }

        private static void WriteExponents(Ac3TestBitWriter writer, int groups, bool gainRange)
        {
            writer.Write(0, 4);
            for (int group = 0; group < groups; group++) { writer.Write(62, 7); }
            if (gainRange) { writer.Write(0, 2); }
        }
    }
}
