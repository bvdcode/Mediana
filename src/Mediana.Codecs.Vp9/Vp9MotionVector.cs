// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Vp9
{
    internal readonly record struct Vp9MotionVector(int X, int Y)
    {
        internal bool HighPrecision => Math.Abs(X) < 64 && Math.Abs(Y) < 64;
        internal Vp9MotionVector Add(Vp9MotionVector other) => new(X + other.X, Y + other.Y);
        internal Vp9MotionVector Negate() => new(-X, -Y);
    }
}
