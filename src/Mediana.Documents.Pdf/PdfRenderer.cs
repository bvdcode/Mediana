// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System;
using System.Numerics;
using System.Threading;
using Mediana.Core.Memory;

namespace Mediana.Documents.Pdf
{
    /// <summary>Rasterizes interpreted PDF pages on the CPU.</summary>
    public static class PdfRenderer
    {
        /// <summary>Plans the output before interpreting the page. JPEG samples are reduced to the device resolution;
        /// temporary decoded images are released when rendering completes. Page extraction retains full resolution.</summary>
        public static PixelBuffer Render(PdfDocument document, int pageNumber, PdfRenderOptions? options = null,
            CancellationToken cancellationToken = default, MediaBufferAllocator? allocator = null)
        {
            ArgumentNullException.ThrowIfNull(document);
            options ??= new();
            PdfRenderPlan plan = PdfRenderPlan.Create(document.GetPageInfo(pageNumber), options);
            using PdfPage page = document.GetPageForRender(pageNumber, plan.Scale * options.Supersampling, cancellationToken);
            return Render(page, options, plan, cancellationToken, allocator);
        }

        /// <summary>Renders the visible page, honoring its crop box and rotation. The allocator counts final output storage.</summary>
        public static PixelBuffer Render(PdfPage page, PdfRenderOptions? options = null,
            CancellationToken cancellationToken = default, MediaBufferAllocator? allocator = null)
        {
            ArgumentNullException.ThrowIfNull(page);
            options ??= new PdfRenderOptions();
            return Render(page, options, PdfRenderPlan.Create(page.Info, options), cancellationToken, allocator);
        }

        private static PixelBuffer Render(PdfPage page, PdfRenderOptions options, PdfRenderPlan plan,
            CancellationToken cancellationToken, MediaBufferAllocator? allocator)
        {
            page.RequireOpen();
            cancellationToken.ThrowIfCancellationRequested();
            if (!options.AllowPartialRendering && page.UnsupportedFeatures.Count != 0)
            {
                throw new NotSupportedException("Page contains unsupported PDF features: " + string.Join("; ", page.UnsupportedFeatures));
            }

            foreach (string warning in page.Warnings)
            {
                options.WarningHandler?.Invoke(warning);
            }
            (int width, int height, double scale) = plan;
            int samples = options.Supersampling;
            PixelBuffer output = allocator is null ? new(width, height) : new(width, height, allocator);
            bool completed = false;
            try
            {
                using PixelBuffer? supersampled = samples == 1 ? null : new(width * samples, height * samples);
                PixelBuffer image = supersampled ?? output;
                image.Clear(default);
                RasterCanvas canvas = new(image);
                Matrix3x2 transform = new((float)(scale * samples), 0, 0, (float)(-scale * samples), 0, (float)(page.Height * scale * samples));
                foreach (IPdfDrawCommand command in page.Commands)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    command.Draw(canvas, transform, cancellationToken);
                }

                image.CompositeBackground(options.Background, cancellationToken);
                supersampled?.DownsampleTo(output, samples, cancellationToken);
                completed = true;
                return output;
            }
            finally
            {
                if (!completed)
                {
                    output.Dispose();
                }
            }
        }

    }
}
