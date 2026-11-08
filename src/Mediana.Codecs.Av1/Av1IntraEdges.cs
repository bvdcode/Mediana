// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly ref struct Av1IntraEdges<T>(Span<T> above, Span<T> left, int width, int height,
        bool haveAbove, bool haveLeft, bool smoothNeighbors) where T : unmanaged
    {
        internal readonly Span<T> Above = above;
        internal readonly Span<T> Left = left;
        internal int Width => width;
        internal int Height => height;
        internal bool HaveAbove => haveAbove;
        internal bool HaveLeft => haveLeft;
        internal bool SmoothNeighbors => smoothNeighbors;
    }
}
