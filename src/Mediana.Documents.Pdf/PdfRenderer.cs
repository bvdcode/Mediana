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
        /// <summary>Renders the visible page, honoring its crop box and rotation. The allocator counts final output storage.</summary>
        public static PixelBuffer Render(PdfPage page, PdfRenderOptions? options = null,
            CancellationToken cancellationToken = default, MediaBufferAllocator? allocator = null)
        {
            ArgumentNullException.ThrowIfNull(page);
            options ??= new PdfRenderOptions();
            cancellationToken.ThrowIfCancellationRequested();
            if (!options.AllowPartialRendering && page.UnsupportedFeatures.Count != 0)
            {
                throw new NotSupportedException("Page contains unsupported PDF features: " + string.Join("; ", page.UnsupportedFeatures));
            }

            (int width, int height, double scale) = Dimensions(page, options);
            int samples = options.Supersampling;
            PixelBuffer output = allocator is null ? new(width, height) : new(width, height, allocator);
            bool completed = false;
            try
            {
                using PixelBuffer? supersampled = samples == 1 ? null : new(width * samples, height * samples);
                PixelBuffer image = supersampled ?? output;
                image.Clear(options.Background);
                RasterCanvas canvas = new(image);
                Matrix3x2 transform = new((float)(scale * samples), 0, 0, (float)(-scale * samples), 0, (float)(page.Height * scale * samples));
                foreach (IPdfDrawCommand command in page.Commands)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    command.Draw(canvas, transform, cancellationToken);
                }

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

        private static (int Width, int Height, double Scale) Dimensions(PdfPage page, PdfRenderOptions options)
        {
            if (!double.IsFinite(options.Dpi) || options.Dpi <= 0 || options.Dpi > 2400 || options.Supersampling < 1 || options.Supersampling > 4)
            {
                throw new ArgumentOutOfRangeException(nameof(options));
            }

            if (options.MaximumWidth <= 0 || options.MaximumHeight <= 0 || options.MaximumSamples <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options));
            }

            double scale = options.Dpi / 72;
            if (options.MaximumWidth is int maximumWidth)
            {
                scale = Math.Min(scale, maximumWidth / page.Width);
            }

            if (options.MaximumHeight is int maximumHeight)
            {
                scale = Math.Min(scale, maximumHeight / page.Height);
            }

            double width = Math.Max(1, Math.Ceiling(page.Width * scale));
            double height = Math.Max(1, Math.Ceiling(page.Height * scale));
            if (!double.IsFinite(width * height) || width * height * options.Supersampling * options.Supersampling > options.MaximumSamples)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "The rendered page exceeds MaximumSamples.");
            }

            return ((int)width, (int)height, scale);
        }
    }
}
