// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System.Numerics;

namespace Mediana.Drawing
{
    internal readonly record struct SceneNoiseOctave(Vector2 Frequency, float Amplitude, int Width, int Height, int OriginX, int OriginY);
}
