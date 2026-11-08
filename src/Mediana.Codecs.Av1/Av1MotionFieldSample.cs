// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly record struct Av1MotionFieldSample(short Row, short Column, byte Distance)
    {
        internal bool Valid => Distance != 0;
    }
}
