// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Av1
{
    internal ref partial struct Av1TileDecoder<T> where T : unmanaged, IBinaryInteger<T>
    {
        private int Palette(int row, int column, int sizeContext, scoped Span<T> colors, int plane = 0, int lumaPalette = 0)
        {
            int context;
            int offset;
            if (plane == 0)
            {
                context = (row > bounds.RowStart && above[column].PaletteCount != 0 ? 1 : 0) +
                    (column > bounds.ColumnStart && left[row & (LeftContexts - 1)].PaletteCount != 0 ? 1 : 0);
                offset = Av1DefaultCdfs.PaletteModeOffset + (sizeContext * 3 + context) * 3;
            }
            else
            {
                context = lumaPalette == 0 ? 0 : 1;
                offset = Av1DefaultCdfs.UvPaletteModeOffset + context * 3;
            }
            if (symbols.ReadSymbol(probabilities.Slice(offset, 3)) == 0) { return 0; }
            int sizeOffset = plane == 0 ? Av1DefaultCdfs.PaletteSizeOffset : Av1DefaultCdfs.UvPaletteSizeOffset;
            int count = symbols.ReadSymbol(probabilities.Slice(sizeOffset + sizeContext * 8, 8)) + 2;
            Span<T> cache = stackalloc T[16];
            int cached = 0;
            if (row % 16 != 0)
            {
                ref Av1BlockContext<T> top = ref above[column];
                for (int index = 0; index < (plane == 0 ? top.PaletteCount : top.UvPaletteCount); index++)
                {
                    cache[cached++] = plane == 0 ? top.Palette[index] : top.UvPalette[index];
                }
            }
            if (column > bounds.ColumnStart)
            {
                ref Av1BlockContext<T> previous = ref left[row & (LeftContexts - 1)];
                for (int index = 0; index < (plane == 0 ? previous.PaletteCount : previous.UvPaletteCount); index++)
                {
                    cache[cached++] = plane == 0 ? previous.Palette[index] : previous.UvPalette[index];
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
                colors[selected++] = T.CreateTruncating(symbols.ReadLiteral(SampleBitDepth));
            }
            int bits = 0;
            if (selected < count)
            {
                bits = SampleBitDepth - 3 + (int)symbols.ReadLiteral(2);
            }
            while (selected < count)
            {
                int delta = (int)symbols.ReadLiteral(bits) + (plane == 0 ? 1 : 0);
                int next = Math.Min((1 << SampleBitDepth) - 1, int.CreateTruncating(colors[selected - 1]) + delta);
                colors[selected++] = T.CreateTruncating(next);
                int remaining = ((1 << SampleBitDepth) - (plane == 0 ? 1 : 0)) - next;
                int remainingBits = remaining <= 1 ? 0 : BitOperations.Log2((uint)(remaining - 1)) + 1;
                bits = Math.Min(bits, remainingBits);
            }
            if (fresh > 0)
            {
                colors[..count].Sort();
            }
            return count;
        }

        private void ReadPaletteTokens(int row, int column, int width, int height, int count, scoped Span<byte> map, int plane = 0)
        {
            int sub = sequence.PlaneShift(plane);
            int visibleWidth = Math.Min(width, (sequence.MiColumns - column) * 4) >> sub;
            int visibleHeight = Math.Min(height, (sequence.MiRows - row) * 4) >> sub;
            width >>= sub;
            height >>= sub;
            if (width < 4) { width += 2; visibleWidth += 2; }
            if (height < 4) { height += 2; visibleHeight += 2; }
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
                    Span<ushort> cdf = PaletteColorCdf(count, context, plane);
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

        private Span<ushort> PaletteColorCdf(int count, int context, int plane)
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
            if (plane != 0) { offset += Av1DefaultCdfs.UvPaletteColor2Offset - Av1DefaultCdfs.PaletteColor2Offset; }
            return probabilities.Slice(offset + context * (count + 1), count + 1);
        }
    }
}
