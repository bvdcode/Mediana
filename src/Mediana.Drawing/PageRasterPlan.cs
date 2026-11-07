// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Drawing
{
    public readonly record struct PageRasterPlan(int Width, int Height, double Scale)
    {
        public static PageRasterPlan Create(double widthPoints, double heightPoints, PageRasterOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (!double.IsFinite(widthPoints) || widthPoints <= 0 || !double.IsFinite(heightPoints) || heightPoints <= 0) { throw new ArgumentOutOfRangeException(nameof(widthPoints)); }
            if (!double.IsFinite(options.Dpi) || options.Dpi <= 0 || options.Supersampling is < 1 or > 4 || options.MaximumWidth is <= 0
                || options.MaximumHeight is <= 0 || options.MaximumSamples <= 0) { throw new ArgumentOutOfRangeException(nameof(options)); }
            double scale = options.Dpi / 72;
            if (options.MaximumWidth is int width) { scale = Math.Min(scale, width / widthPoints); }
            if (options.MaximumHeight is int height) { scale = Math.Min(scale, height / heightPoints); }
            double w = Math.Ceiling(widthPoints * scale), h = Math.Ceiling(heightPoints * scale);
            if (!double.IsFinite(w) || !double.IsFinite(h) || w < 1 || h < 1 || w * h * options.Supersampling * options.Supersampling > options.MaximumSamples)
            { throw new InvalidDataException("Document page exceeds MaximumSamples at the requested resolution."); }
            return new(checked((int)w), checked((int)h), scale);
        }
    }
}
