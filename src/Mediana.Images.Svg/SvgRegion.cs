// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
namespace Mediana.Images.Svg
{
    internal record SvgRegion : SceneRegion
    {
        internal float ViewWidth { get; }
        internal float ViewHeight { get; }
        internal SvgRegion(string x, string y, string width, string height, bool objectBox, float viewWidth, float viewHeight)
            : base(SvgLength.Parse(x, objectBox ? 1 : viewWidth), SvgLength.Parse(y, objectBox ? 1 : viewHeight), SvgLength.Parse(width, objectBox ? 1 : viewWidth), SvgLength.Parse(height, objectBox ? 1 : viewHeight), objectBox)
        {
            ViewWidth = viewWidth;
            ViewHeight = viewHeight;
        }
    }
}
