// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Drawing
{
    /// <summary>An immutable inline visual measured in em units. Drawing uses a top-left origin;
    /// the baseline lies at Ascent. Instances must support concurrent drawing to independent canvases.</summary>
    public abstract class InlineGraphic
    {
        public abstract double Advance { get; }
        public abstract double Ascent { get; }
        public abstract double Descent { get; }
        public abstract void Draw(RasterCanvas canvas, Matrix3x2 transform, CancellationToken cancellationToken = default);
    }
}
