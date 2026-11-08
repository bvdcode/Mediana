// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Av1
{
    internal ref partial struct Av1TileDecoder
    {
        private int Palette(int row, int column, int sizeContext, scoped Span<byte> colors)
        {
            int context = (row > 0 && above[column].PaletteCount != 0 ? 1 : 0) +
                (column > 0 && left[row & (LeftContexts - 1)].PaletteCount != 0 ? 1 : 0);
            if (symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.PaletteModeOffset + (sizeContext * 3 + context) * 3, 3)) == 0)
            {
                return 0;
            }
            int count = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.PaletteSizeOffset + sizeContext * 8, 8)) + 2;
            Span<byte> cache = stackalloc byte[16];
            int cached = 0;
            if (row % 16 != 0)
            {
                ref Av1BlockContext top = ref above[column];
                for (int index = 0; index < top.PaletteCount; index++)
                {
                    cache[cached++] = top.Palette[index];
                }
            }
            if (column > 0)
            {
                ref Av1BlockContext previous = ref left[row & (LeftContexts - 1)];
                for (int index = 0; index < previous.PaletteCount; index++)
                {
                    cache[cached++] = previous.Palette[index];
                }
            }
            cache[..cached].Sort();
            int selected = 0;
            for (int index = 0; index < cached && selected < count; index++)
            {
                if (index != 0 && cache[index] == cache[index - 1])
                {
                    continue;
                }
                if (symbols.ReadBoolean())
                {
                    colors[selected++] = cache[index];
                }
            }
            int fresh = selected;
            if (selected < count)
            {
                colors[selected++] = (byte)symbols.ReadLiteral(8);
            }
            int bits = 0;
            if (selected < count)
            {
                bits = 5 + (int)symbols.ReadLiteral(2);
            }
            while (selected < count)
            {
                int delta = (int)symbols.ReadLiteral(bits) + 1;
                int next = Math.Min(255, colors[selected - 1] + delta);
                colors[selected++] = (byte)next;
                int remaining = 255 - next;
                int remainingBits = remaining <= 1 ? 0 : BitOperations.Log2((uint)(remaining - 1)) + 1;
                bits = Math.Min(bits, remainingBits);
            }
            if (fresh > 0)
            {
                colors[..count].Sort();
            }
            return count;
        }

        private void ReadPaletteTokens(int row, int column, int width, int height, int count, scoped Span<byte> map)
        {
            int visibleWidth = Math.Min(width, (sequence.MiColumns - column) * 4);
            int visibleHeight = Math.Min(height, (sequence.MiRows - row) * 4);
            map[0] = (byte)symbols.ReadNs(count);
            Span<byte> order = stackalloc byte[8];
            Span<byte> scores = stackalloc byte[8];
            for (int diagonal = 1; diagonal < visibleWidth + visibleHeight - 1; diagonal++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int x = Math.Min(diagonal, visibleWidth - 1); x >= Math.Max(0, diagonal - visibleHeight + 1); x--)
                {
                    int y = diagonal - x;
                    for (int index = 0; index < order.Length; index++)
                    {
                        order[index] = (byte)index;
                    }
                    scores.Clear();
                    if (x > 0) { scores[map[y * width + x - 1]] += 2; }
                    if (y > 0) { scores[map[(y - 1) * width + x]] += 2; }
                    if (x > 0 && y > 0) { scores[map[(y - 1) * width + x - 1]]++; }
                    for (int index = 0; index < 3; index++)
                    {
                        int maximum = index;
                        for (int candidate = index + 1; candidate < count; candidate++)
                        {
                            if (scores[candidate] > scores[maximum])
                            {
                                maximum = candidate;
                            }
                        }
                        byte score = scores[maximum];
                        byte color = order[maximum];
                        for (int candidate = maximum; candidate > index; candidate--)
                        {
                            scores[candidate] = scores[candidate - 1];
                            order[candidate] = order[candidate - 1];
                        }
                        scores[index] = score;
                        order[index] = color;
                    }
                    int hash = scores[0] + 2 * scores[1] + 2 * scores[2];
                    int context = hash switch
                    {
                        2 => 0,
                        5 => 4,
                        6 => 3,
                        7 => 2,
                        8 => 1,
                        _ => throw new InvalidDataException("The AV1 palette color context is invalid.")
                    };
                    Span<ushort> cdf = PaletteColorCdf(count, context);
                    map[y * width + x] = order[symbols.ReadSymbol(cdf)];
                }
            }
            for (int y = 0; y < visibleHeight; y++)
            {
                map.Slice(y * width + visibleWidth, width - visibleWidth).Fill(map[y * width + visibleWidth - 1]);
            }
            for (int y = visibleHeight; y < height; y++)
            {
                map.Slice((visibleHeight - 1) * width, width).CopyTo(map.Slice(y * width, width));
            }
        }

        private Span<ushort> PaletteColorCdf(int count, int context)
        {
            int offset = count switch
            {
                2 => Av1DefaultCdfs.PaletteColor2Offset,
                3 => Av1DefaultCdfs.PaletteColor3Offset,
                4 => Av1DefaultCdfs.PaletteColor4Offset,
                5 => Av1DefaultCdfs.PaletteColor5Offset,
                6 => Av1DefaultCdfs.PaletteColor6Offset,
                7 => Av1DefaultCdfs.PaletteColor7Offset,
                8 => Av1DefaultCdfs.PaletteColor8Offset,
                _ => throw new InvalidDataException("The AV1 palette size is invalid.")
            };
            return probabilities.Slice(offset + context * (count + 1), count + 1);
        }
    }
}
