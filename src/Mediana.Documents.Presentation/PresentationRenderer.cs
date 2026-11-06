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
            if (options.MaximumImagePixels <= 0) { throw new ArgumentOutOfRangeException(nameof(options)); }
            PageRasterPlan plan = PageRasterPlan.Create(document.Width,document.Height,options);
            cancellationToken.ThrowIfCancellationRequested();
            PresentationScene scene = new(document, slideIndex, cancellationToken);
            allocator ??= new(long.MaxValue);
            PixelBuffer output = new(plan.Width,plan.Height,allocator);
            bool completed = false;
            try
            {
                int samples = options.Supersampling;
                using PixelBuffer? high = samples == 1 ? null : new(output.Width * samples, output.Height * samples, allocator);
                PixelBuffer surface = high ?? output;
                surface.Clear(new(255, 255, 255));
                using PresentationRasterizer rasterizer = new(new(surface), document, scene, options, allocator, cancellationToken);
                await rasterizer.DrawAsync(Matrix3x2.CreateScale((float)(plan.Scale * samples))).ConfigureAwait(false);
                high?.DownsampleTo(output, samples, cancellationToken);
                completed = true;
                return output;
            }
            finally { if (!completed) { output.Dispose(); } }
        }
    }
}
