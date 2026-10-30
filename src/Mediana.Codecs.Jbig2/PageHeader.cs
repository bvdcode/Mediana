// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Jbig2
{
    internal record PageHeader(int Width, int Height, int Flags)
    {
        internal static PageHeader Read(Segment segment, BitReader input, DecodeBudget budget)
        {
            int width = budget.Count(input.U32(), budget.Options.MaximumPixels);
            int height = budget.Count(input.U32(), budget.Options.MaximumPixels);
            input.Take(8);
            int flags = input.Byte();
            input.U16();
            if (segment.Page == 0 || width == 0 || height == 0 || (long)width * height > budget.Options.MaximumPixels)
            {
                throw new InvalidDataException("Invalid JBIG2 page information or MaximumPixels exceeded.");
            }
            if ((budget.Options.ExpectedWidth is int w && w != width) || (budget.Options.ExpectedHeight is int h && h != height))
            {
                throw new InvalidDataException("JBIG2 page dimensions differ from the required dimensions.");
            }
            return new PageHeader(width, height, flags);
        }
    }
}
