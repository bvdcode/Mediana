// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Webp
{
    internal class Vp8BooleanWriter(WebpByteBuffer destination)
    {
        private uint range = 255;
        private uint lower;
        private int remaining = 24;

        internal void Write(bool value, int probability = 128)
        {
            uint split = 1 + (((range - 1) * (uint)probability) >> 8);
            if (value) { lower += split; range -= split; }
            else { range = split; }
            while (range < 128)
            {
                range <<= 1;
                if ((lower & 0x80000000) != 0) { Carry(); }
                lower <<= 1;
                if (--remaining == 0)
                {
                    destination.Add((byte)(lower >> 24));
                    lower &= 0x00ffffff;
                    remaining = 8;
                }
            }
        }

        internal void Literal(int value, int bits)
        {
            for (int bit = bits - 1; bit >= 0; bit--) { Write(((value >> bit) & 1) != 0); }
        }

        internal void Finish()
        {
            ulong final = (ulong)lower << remaining;
            if (final >> 32 != 0) { Carry(); }
            destination.Add((byte)(final >> 24));
            destination.Add((byte)(final >> 16));
            destination.Add((byte)(final >> 8));
            destination.Add((byte)final);
        }

        private void Carry()
        {
            int index = destination.Length - 1;
            while (index >= 0 && destination[index] == 255) { destination[index--] = 0; }
            if (index < 0) { throw new InvalidOperationException("VP8 arithmetic carry exceeds the partition."); }
            destination[index]++;
        }
    }
}
