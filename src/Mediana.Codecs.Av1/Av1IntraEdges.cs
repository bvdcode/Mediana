// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly ref struct Av1IntraEdges(Span<byte> above, Span<byte> left, int width, int height,
        bool haveAbove, bool haveLeft, bool smoothNeighbors)
    {
        internal readonly Span<byte> Above = above;
        internal readonly Span<byte> Left = left;
        internal int Width => width;
        internal int Height => height;
        internal bool HaveAbove => haveAbove;
        internal bool HaveLeft => haveLeft;
        internal bool SmoothNeighbors => smoothNeighbors;
    }
}
