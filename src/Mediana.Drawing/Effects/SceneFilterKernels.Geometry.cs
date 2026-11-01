// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
namespace Mediana.Drawing
{
    internal static partial class SceneFilterKernels
    {
        internal static PixelBuffer Gaussian(PixelBuffer input, Vector2 sigma, Matrix3x2 units, bool linear, string edge, SceneRenderContext context, bool overwrite = false)
        {
            if (sigma.X < 0 || sigma.Y < 0)
            {
                throw new InvalidDataException("Negative Drawing blur deviation.");
            }

            Vector2 x = Vector2.TransformNormal(new Vector2(sigma.X, 0), units), y = Vector2.TransformNormal(new Vector2(0, sigma.Y), units);
            if (x.Length() < .01f && y.Length() < .01f)
            {
                return overwrite ? input : ScenePixels.Copy(input, context);
            }

            PixelBuffer first = GaussianAxis(input, x, linear, edge, context);
            if (overwrite)
            {
                context.Release(input);
            }

            PixelBuffer result = GaussianAxis(first, y, linear, edge, context);
            context.Release(first);
            return result;
        }
        internal static PixelBuffer GaussianAxis(PixelBuffer input, Vector2 axis, bool linear, string edge, SceneRenderContext context)
        {
            float sigma = axis.Length();
            if (sigma < .01f)
            {
                return ScenePixels.Copy(input, context);
            }

            if (!float.IsFinite(sigma) || sigma > 256)
            {
                throw new InvalidDataException("Drawing blur deviation exceeds 256 device samples.");
            }

            int radius = (int)MathF.Ceiling(sigma * 3);
            using IDisposable kernelBudget = context.Reserve((radius * 2) + 1, 1, 1);
            float[]? kernel = new float[(radius * 2) + 1];
            float total = 0;
            for (int i = -radius; i <= radius; i++)
            {
                context.Token.ThrowIfCancellationRequested();
                float weight = MathF.Exp(-(i * i) / (2 * sigma * sigma));
                kernel[i + radius] = weight;
                total += weight;
            }

            for (int i = 0; i < kernel.Length; i++)
            {
                kernel[i] /= total;
            }

            Vector2 direction = axis / sigma;
            if (axis.X == 0 || axis.Y == 0)
            {
                return GaussianAligned(input, kernel, axis.Y == 0, axis.X + axis.Y < 0, linear, edge, context);
            }

            return GaussianRotated(input, direction, kernel, linear, edge, context);
        }
        internal static PixelBuffer MorphAxis(PixelBuffer input, Vector2 axis, bool dilate, bool linear, SceneRenderContext context)
        {
            float length = axis.Length();
            if (length > 256)
            {
                throw new InvalidDataException("Drawing morphology radius exceeds 256 device samples.");
            }

            int radius = (int)MathF.Floor(length);
            if (axis.X == 0 || axis.Y == 0)
            {
                return MorphAligned(input, radius, axis.Y == 0, axis.X + axis.Y < 0, dilate, linear, context);
            }

            Vector2 direction = length == 0 ? Vector2.Zero : axis / length;
            return ScenePixels.Map(input, (x, y) =>
            {
                Vector4 result = dilate ? Vector4.Zero : Vector4.One;
                for (int i = -radius; i <= radius; i++)
                {
                    Vector4 sample = ScenePixels.Sample(input, x + (direction.X * i), y + (direction.Y * i), linear);
                    result = dilate ? Vector4.Max(result, sample) : Vector4.Min(result, sample);
                }

                return result;
            }, linear, context);
        }
        internal static PixelBuffer Morphology(Vector2 radius, bool dilate, PixelBuffer input, Matrix3x2 units, bool linear, SceneRenderContext context, bool overwrite)
        {
            if (radius.X < 0 || radius.Y < 0)
            {
                throw new InvalidDataException("Negative Drawing morphology radius.");
            }

            Vector2 xAxis = Vector2.TransformNormal(new Vector2(radius.X, 0), units), yAxis = Vector2.TransformNormal(new Vector2(0, radius.Y), units);
            PixelBuffer first = MorphAxis(input, xAxis, dilate, linear, context);
            if (overwrite)
            {
                context.Release(input);
            }

            PixelBuffer result = MorphAxis(first, yAxis, dilate, linear, context);
            context.Release(first);
            return result;
        }
    }
}
