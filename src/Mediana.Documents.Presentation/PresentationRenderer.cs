// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Drawing;
using System.Numerics;
namespace Mediana.Documents.Presentation
{
    /// <summary>Rasterizes PresentationML directly through Drawing, without an intermediate document format.</summary>
    public static class PresentationRenderer
    {
        /// <summary>Renders a zero-based slide. The caller owns the returned buffer; nested pictures share its allocation budget.</summary>
        public static async ValueTask<PixelBuffer> RenderAsync(PresentationDocument document, int slideIndex,
            PresentationRenderOptions? options = null, CancellationToken cancellationToken = default, MediaBufferAllocator? allocator = null)
        {
            ArgumentNullException.ThrowIfNull(document);
            document.CheckSlide(slideIndex);
            options ??= new();
            if (!double.IsFinite(options.Dpi) || options.Dpi <= 0 || options.Supersampling is < 1 or > 4 || options.MaximumWidth is <= 0
                || options.MaximumHeight is <= 0 || options.MaximumSamples <= 0 || options.MaximumImagePixels <= 0) { throw new ArgumentOutOfRangeException(nameof(options)); }
            double scale = options.Dpi / 72;
            if (options.MaximumWidth is int mw) { scale = Math.Min(scale, mw / document.Width); }
            if (options.MaximumHeight is int mh) { scale = Math.Min(scale, mh / document.Height); }
            double width = Math.Ceiling(document.Width * scale), height = Math.Ceiling(document.Height * scale);
            if (width < 1 || height < 1 || width * height * options.Supersampling * options.Supersampling > options.MaximumSamples)
            { throw new InvalidDataException("Presentation slide exceeds MaximumSamples at the requested resolution."); }
            cancellationToken.ThrowIfCancellationRequested();
            PresentationScene scene = new(document, slideIndex, cancellationToken);
            allocator ??= new(long.MaxValue);
            PixelBuffer output = new(checked((int)width), checked((int)height), allocator);
            bool completed = false;
            try
            {
                int samples = options.Supersampling;
                using PixelBuffer? high = samples == 1 ? null : new(output.Width * samples, output.Height * samples, allocator);
                PixelBuffer surface = high ?? output;
                surface.Clear(new(255, 255, 255));
                using PresentationRasterizer rasterizer = new(new(surface), document, scene, options, allocator, cancellationToken);
                await rasterizer.DrawAsync(Matrix3x2.CreateScale((float)(scale * samples))).ConfigureAwait(false);
                high?.DownsampleTo(output, samples, cancellationToken);
                completed = true;
                return output;
            }
            finally { if (!completed) { output.Dispose(); } }
        }
    }
}
