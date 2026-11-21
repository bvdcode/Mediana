// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Video;

namespace Mediana.Codecs.ProRes
{
    internal static class ProResAlpha
    {
        public static bool Decode(ReadOnlySpan<byte> encoded, int type, Span<byte> output, PlaneLayout layout,
            int x, int y, int width, int height, int parity, int rowStep, bool allowLegacy)
        {
            int depth = type switch
            {
                1 => 8,
                2 => 16,
                _ => throw new InvalidDataException("The ProRes alpha type is reserved.")
            };
            int mask = (1 << depth) - 1;
            int previous = mask;
            int position = 0;
            ProResBitReader bits = new(encoded);
            int visibleSamples = width * height;
            int maximumSamples = allowLegacy ? width * 16 : visibleSamples;
            bool recovered = false;
            while (position < visibleSamples || (allowLegacy && !bits.EndOfData && position < maximumSamples))
            {
                int difference;
                if (bits.Read(1) != 0)
                {
                    difference = (int)bits.Read(depth);
                }
                else
                {
                    int magnitude = (int)bits.Read(depth == 8 ? 3 : 6) + 1;
                    difference = bits.Read(1) == 0 ? magnitude : -magnitude;
                }
                int value = (previous + difference) & mask;
                previous = value;
                int run = 1;
                if (allowLegacy && bits.EndOfData && (position + 1 == visibleSamples || position + 1 == maximumSamples))
                {
                    run = 1;
                    recovered = true;
                }
                else if (bits.Read(1) == 0)
                {
                    run = (int)bits.Read(4);
                    if (run == 0)
                    {
                        run = (int)bits.Read(11);
                    }
                    run++;
                }
                if (run > maximumSamples - position)
                {
                    throw new InvalidDataException("The ProRes alpha run exceeds the slice.");
                }
                ushort sample = (ushort)(depth == 8 ? value * 257 : value);
                while (run > 0)
                {
                    int column = position % width;
                    int row = position / width;
                    int take = Math.Min(run, width - column);
                    int visible = Math.Max(0, Math.Min(take, layout.RowBytes / 2 - x - column));
                    int target = layout.Offset + ((y + row) * rowStep + parity) * layout.Stride + (x + column) * 2;
                    if (visible != 0 && row < height)
                    {
                        MemoryMarshal.Cast<byte, ushort>(output.Slice(target, visible * 2)).Fill(sample);
                    }
                    position += take;
                    run -= take;
                }
            }
            recovered |= position > visibleSamples;
            if (!bits.EndOfData)
            {
                throw new InvalidDataException("The ProRes alpha padding is nonzero.");
            }
            return recovered;
        }
    }
}
