// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Jpeg2000
{
    internal record ComponentSize(int X0, int Y0, int Width, int Height, int BitDepth, bool IsSigned,
        int HorizontalSampling, int VerticalSampling)
    {
        internal Jpeg2000Component Allocate(DecodeBudget budget)
        {
            budget.Reserve((long)Width * Height * sizeof(int));
            return new Jpeg2000Component(X0, Y0, Width, Height, BitDepth, IsSigned,
                HorizontalSampling, VerticalSampling, new int[checked(Width * Height)]);
        }
    }
}
