// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
namespace Mediana.Drawing
{
    internal abstract record SceneEffect
    {
        internal abstract PixelBuffer Apply(PixelBuffer source, (float X, float Y, float Width, float Height) box, Matrix3x2 matrix, SceneRenderContext context, PixelBuffer background, ScenePaint? fill, ScenePaint? stroke, float fillOpacity, float strokeOpacity);
    }
}
