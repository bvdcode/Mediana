// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Word
{
    internal readonly record struct WordRenderPlan(int Width, int Height, double Scale)
    {
        internal static WordRenderPlan Create(WordPageSize page, WordRenderOptions options)
        {
            if (!double.IsFinite(options.Dpi) || options.Dpi <= 0 || options.Supersampling is < 1 or > 4
                || options.MaximumWidth is <= 0 || options.MaximumHeight is <= 0 || options.MaximumSamples <= 0 || options.MaximumImagePixels <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options));
            }
            double scale = options.Dpi / 72;
            if (options.MaximumWidth is int maxWidth) { scale = Math.Min(scale, maxWidth / page.Width); }
            if (options.MaximumHeight is int maxHeight) { scale = Math.Min(scale, maxHeight / page.Height); }
            double width = Math.Ceiling(page.Width * scale), height = Math.Ceiling(page.Height * scale);
            if (!double.IsFinite(width) || !double.IsFinite(height) || width < 1 || height < 1
                || width * height * options.Supersampling * options.Supersampling > options.MaximumSamples)
            {
                throw new InvalidDataException("Word page exceeds MaximumSamples at the requested resolution.");
            }
            return new(checked((int)width), checked((int)height), scale);
        }
    }
}
