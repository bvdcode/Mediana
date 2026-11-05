// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Images.Svg;
using System.Numerics;

namespace Mediana.Drawing.Emoji.Fluent
{
    internal class FluentEmojiGraphic(SvgDocument document) : InlineGraphic
    {
        public override double Advance => 1;
        public override double Ascent => .85;
        public override double Descent => .15;

        public override void Draw(RasterCanvas canvas, Matrix3x2 transform, CancellationToken cancellationToken = default) =>
            SvgRenderer.Draw(document, canvas, Matrix3x2.CreateScale(1 / document.Width, 1 / document.Height) * transform,
                cancellationToken: cancellationToken);
    }
}
