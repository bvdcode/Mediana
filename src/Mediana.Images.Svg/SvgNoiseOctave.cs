// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System.Numerics;

namespace Mediana.Images.Svg
{
    internal readonly record struct SvgNoiseOctave(Vector2 Frequency, float Amplitude, int Width, int Height, int OriginX, int OriginY);
}
