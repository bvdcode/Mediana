// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Pdf
{
    internal readonly record struct PdfRenderPlan(int Width, int Height, double Scale)
    {
        internal static PdfRenderPlan Create(PdfPageInfo page, PdfRenderOptions options)
        {
            if (!double.IsFinite(options.Dpi) || options.Dpi <= 0 || options.Dpi > 2400 || options.Supersampling is < 1 or > 4
                || options.MaximumWidth <= 0 || options.MaximumHeight <= 0 || options.MaximumSamples <= 0 || options.MaximumWorkingBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options));
            }
            double scale = options.Dpi / 72;
            if (options.MaximumWidth is int maximumWidth) { scale = Math.Min(scale, maximumWidth / page.Width); }
            if (options.MaximumHeight is int maximumHeight) { scale = Math.Min(scale, maximumHeight / page.Height); }
            double width = Math.Max(1, Math.Ceiling(page.Width * scale));
            double height = Math.Max(1, Math.Ceiling(page.Height * scale));
            if (!double.IsFinite(width * height) || width * height * options.Supersampling * options.Supersampling > options.MaximumSamples)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "The rendered page exceeds MaximumSamples.");
            }
            return new((int)width, (int)height, scale);
        }
    }
}
