// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Audio;

namespace Mediana.Codecs.Ac3.Tests
{
    internal static class Ac3DependentFrame
    {
        public static byte[] CreateSequence(bool wide, bool dynamicRange = false)
        {
            List<byte> data = [];
            for (int group = 0; group < 4; group++)
            {
                for (int component = 0; component < (wide ? 4 : 2); component++)
                { data.AddRange(Create(component, group, wide, dynamicRange)); }
            }
            return data.ToArray();
        }

        public static byte[] Create(int component, int group = 0, bool wide = false, bool dynamicRange = false, bool customMap = true)
        {
            const int blocks = 3;
            int mode = component switch { 0 => 7, 1 or 2 => 6, 3 => 3, _ => throw new ArgumentOutOfRangeException(nameof(component)) };
            bool lfe = component is 0 or 3;
            int channels = Ac3ChannelLayout.GetChannelCount(mode, lfe), full = channels - (lfe ? 1 : 0);
            int last = wide ? 3 : 1;
            int frameBytes = component switch { 0 => 2560, 1 or 2 => 2048, 3 => 1536, _ => throw new InvalidOperationException() };
            Ac3TestBitWriter writer = new(frameBytes);
            writer.Write(0xb77, 16); writer.Write(component == 0 ? 0 : 1, 2);
            writer.Write(component == 0 ? 0 : component - 1, 3); writer.Write(frameBytes / 2 - 1, 11);
            writer.Write(0, 2); writer.Write(2, 2); writer.Write(mode, 3); writer.Write(lfe ? 1 : 0, 1); writer.Write(16, 5);
            writer.Write(31, 5); writer.Write(component == last ? 1 : 0, 1);
            if (component == last) { writer.Write(0, 8); }
            if (component != 0)
            {
                writer.Write(customMap ? 1 : 0, 1);
                if (customMap) { writer.Write(component switch { 1 => 0x1a00, 2 => 0x0410, 3 => 0x018a, _ => throw new InvalidOperationException() }, 16); }
            }
            writer.Write(0, 2);
            if (component == 0) { writer.Write(0, 1); }
            writer.Write(0, 1);
            writer.Write(0, 2); writer.Write(0, 1); writer.Write(1, 1); writer.Write(1, 1); writer.Write(0, 5);
            writer.Write(0, blocks);
            for (int block = 0; block < blocks; block++)
            { for (int channel = 0; channel < full; channel++) { writer.Write(block == 0 ? 1 : 0, 2); } }
            if (lfe) { for (int block = 0; block < blocks; block++) { writer.Write(block == 0 ? 1 : 0, 1); } }
            if (component == 0) { writer.Write(0, 1); }
            writer.Write(63, 6); writer.Write(15, 4); writer.Write(0, 1);
            for (int block = 0; block < blocks; block++)
            {
                writer.Write(0, full * 2);
                bool dynamic = dynamicRange && block == 0 && (component == 0 || component == last);
                writer.Write(dynamic ? 1 : 0, 1);
                if (dynamic) { writer.Write(component == 0 ? 0xe0 : 0x20, 8); }
                writer.Write(0, 1);
                if (block == 0)
                {
                    writer.Write(0, full * 6);
                    for (int channel = 0; channel < channels; channel++)
                    {
                        writer.Write(0, 4);
                        for (int exponent = 0; exponent < (channel < full ? 24 : 2); exponent++) { writer.Write(62, 7); }
                        if (channel < full) { writer.Write(0, 2); }
                    }
                }
                if (component == 0) { writer.Write(0, 1); }
                for (int channel = 0; channel < channels; channel++)
                {
                    for (int bin = 0; bin < (channel < full ? 73 : 7); bin++)
                    { writer.Write(Coefficient(bin, group, block, component, channel), 16); }
                }
            }
            return writer.Finish(true);
        }

        internal static int Coefficient(int bin, int group, int block, int component, int channel) => bin switch
        {
            3 => (component + 1) * 17 + channel * 3 + group * 2 + block,
            19 => -(component + 1) * 7 - channel - group,
            _ => 0
        };
    }
}
