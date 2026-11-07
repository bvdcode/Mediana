// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Vp8
{
    internal readonly record struct Vp8MotionVector(int X, int Y)
    {
        internal Vp8MotionVector Add(Vp8MotionVector other) => new(X + other.X, Y + other.Y);
        internal Vp8MotionVector Negate() => new(-X, -Y);
        internal Vp8MotionVector Clamp(int column, int row, int columns, int rows) =>
            new(Math.Clamp(X, -(column + 1) * 128, (columns - column) * 128),
                Math.Clamp(Y, -(row + 1) * 128, (rows - row) * 128));
    }
}
