// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Numerics;
using System.Threading;
namespace Mediana.Drawing
{
    internal static class SceneRasterizer
    {
        internal static PixelBuffer Render(SceneNode root, float intrinsicWidth, float intrinsicHeight, DrawingRenderOptions options, Func<int, int, Matrix3x2> viewport, CancellationToken token)
        {
            if (options.Width <= 0 || options.Height <= 0 || options.Supersampling < 1 || options.Supersampling > 4 || options.MaximumSamples <= 0 || options.MaximumEffectBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options));
            }
            double width = options.Width ?? Math.Ceiling(options.Height.HasValue ? (options.Height.Value * (double)intrinsicWidth) / intrinsicHeight : intrinsicWidth);
            double height = options.Height ?? Math.Ceiling(options.Width.HasValue ? (options.Width.Value * (double)intrinsicHeight) / intrinsicWidth : intrinsicHeight);
            if (!double.IsFinite(width * height) || width * height * options.Supersampling * options.Supersampling > options.MaximumSamples)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "Drawing output exceeds MaximumSamples.");
            }
            int w = (int)Math.Max(1, width), h = (int)Math.Max(1, height);
            int factor = options.Supersampling;
            PixelBuffer image = new(w * factor, h * factor);
            image.Clear(options.Background);
            Matrix3x2 matrix = viewport(w, h) * Matrix3x2.CreateScale(factor);
            using SceneRenderContext context = new(options, token);
            root.Draw(new RasterCanvas(image), matrix, context);
            return factor == 1 ? image : image.Downsample(factor, token);
        }
    }
}
