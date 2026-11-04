// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Formats.Video;

namespace Mediana.Tests
{
    internal class H264ResidualRegionReader(H264Picture picture, int x, int y, int pattern, int style) : IH264ResidualReader
    {
        private int next;
        public int Calls { get; private set; }

        public static (int X, int Y) Position(int index) =>
            (index / 4 % 2 * 8 + index % 2 * 4, index / 8 * 8 + index % 4 / 2 * 4);

        public static int Count(int index, int style) => style switch
        {
            0 => 0,
            1 => 1,
            2 => index & 1,
            _ => throw new ArgumentOutOfRangeException(nameof(style))
        };

        public static int Level(int index) => (index & 2) == 0 ? -2 : 3;

        public int Read(ref H264BitReader bits, H264ResidualCategory category, int plane, int sampleX, int sampleY,
            scoped Span<int> coefficients)
        {
            while (next < 16 && (pattern & (1 << (next / 4))) == 0)
            {
                next++;
            }
            (int dx, int dy) = Position(next);
            if (next == 16 || category != H264ResidualCategory.Luma4x4 || plane != 0
                || sampleX != x + dx || sampleY != y + dy || coefficients.Length != 16
                || coefficients.ContainsAnyExcept(0))
            {
                throw new InvalidDataException("Residual reconstruction changed entropy order, coordinates or coefficient initialization.");
            }
            for (int index = 0; index < next; index++)
            {
                (int px, int py) = Position(index);
                int position = (y + py) / 4 * (picture.Width / 4) + (x + px) / 4;
                int expectedCount = (pattern & (1 << (index / 4))) == 0 ? 0 : Count(index, style);
                int expectedMode = (128 + position % 10) & 127;
                if (picture.Counts(0)[position] != expectedCount || picture.Modes[position] != expectedMode)
                {
                    throw new InvalidDataException("Residual reconstruction changed completed neighbor metadata before an entropy read.");
                }
            }
            int count = Count(next, style);
            if (count != 0)
            {
                coefficients[0] = Level(next);
            }
            if (!bits.ReadFlag())
            {
                throw new InvalidDataException("Residual reconstruction consumed an unexpected input bit.");
            }
            Calls++;
            next++;
            return count;
        }
    }
}
