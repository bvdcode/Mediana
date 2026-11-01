// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Numerics;
namespace Mediana.Drawing
{
    internal record SceneFilterInput(PixelBuffer Input, PixelBuffer Second, PixelBuffer Source,
        Matrix3x2 Units, Matrix3x2 Matrix, (float X, float Y, float Width, float Height) Region, (float X, float Y, float Width, float Height) InputRegion,
        bool Linear, SceneRenderContext Context, bool Overwrite, Func<string, PixelBuffer> Resolve);
}
