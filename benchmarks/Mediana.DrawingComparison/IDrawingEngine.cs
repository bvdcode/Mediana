// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.DrawingComparison
{
    internal interface IDrawingEngine
    {
        IRenderedImage Render(ComparisonScene scene);
    }
}
