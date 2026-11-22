// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System;
using System.Numerics;
using System.Threading.Tasks;

namespace Mediana.Drawing
{
    internal static partial class SceneFilterKernels
    {
        private static PixelBuffer GaussianRotated(PixelBuffer input, Vector2 direction, float[] kernel, bool linear, string edge, SceneRenderContext context)
        {
            ScenePixelRegion source = ScenePixelRegion.AlphaBounds(input, context.Token);
            PixelBuffer result = context.CreateBuffer(input.Width, input.Height);
            if (source.Empty)
            {
                return result;
            }

            int radius = kernel.Length / 2;
            ScenePixelRegion target = edge == "none"
                ? source.Expand(Vector2.Abs(direction) * radius, input.Width, input.Height)
                : new ScenePixelRegion(0, 0, input.Width, input.Height);
            int sampleLeft = edge == "none" ? source.Left : 0;
            int sampleWidth = edge == "none" ? source.Width : input.Width;
            int padding = Math.Max(3, (int)MathF.Ceiling(MathF.Abs(direction.X) * radius) + 1);
            int workers = Math.Min(Environment.ProcessorCount, target.Height);
            using IDisposable reservation = context.Reserve(checked(2 * (sampleWidth + 2 * padding) + target.Width), workers, 4);
            Parallel.For(target.Top, target.Bottom,
                new ParallelOptions { CancellationToken = context.Token, MaxDegreeOfParallelism = workers },
                () => new SceneGaussianScratch(input, sampleLeft, sampleWidth, padding, target.Width, linear, edge, context.Token),
                (y, _, scratch) =>
                {
                    Span<Vector4> sums = scratch.Sums;
                    sums.Clear();
                    int first = -radius, last = radius;
                    if (edge == "none")
                    {
                        IntersectKernel(y, direction.Y, source.Top, source.Bottom, ref first, ref last);
                    }
                    for (int i = first; i <= last; i++)
                    {
                        context.Token.ThrowIfCancellationRequested();
                        float sampleY = y + direction.Y * i;
                        int iy = (int)MathF.Floor(sampleY);
                        float fy = sampleY - iy, shift = direction.X * i;
                        scratch.Rows(iy, out ReadOnlySpan<Vector4> upper, out ReadOnlySpan<Vector4> lower);
                        int left = target.Left, right = target.Right;
                        if (edge == "none")
                        {
                            left = Math.Max(left, (int)MathF.Floor(source.Left - shift) - 2);
                            right = Math.Min(right, (int)MathF.Ceiling(source.Right - shift) + 1);
                        }
                        float weight = kernel[i + radius];
                        for (int x = left; x < right; x++)
                        {
                            if ((x & 1023) == 0) { context.Token.ThrowIfCancellationRequested(); }
                            float sampleX = x + shift;
                            int ix = (int)MathF.Floor(sampleX);
                            float fx = sampleX - ix;
                            int offset = ix - sampleLeft + padding;
                            Vector4 sample = fx == 0 && fy == 0 ? upper[offset]
                                : Vector4.Lerp(Vector4.Lerp(upper[offset], upper[offset + 1], fx),
                                    Vector4.Lerp(lower[offset], lower[offset + 1], fx), fy);
                            sums[x - target.Left] += sample * weight;
                        }
                    }
                    Span<byte> output = result.Pixels.Span.Slice(y * input.Width * 4, input.Width * 4);
                    for (int x = target.Left; x < target.Right; x++)
                    {
                        ScenePixels.Write(output, x * 4, sums[x - target.Left], linear);
                    }
                    return scratch;
                }, _ => { });
            return result;
        }

        private static void IntersectKernel(int pixel, float direction, int minimum, int maximum, ref int first, ref int last)
        {
            float a = (minimum - 1f - pixel) / direction, b = (maximum - pixel) / direction;
            first = (int)Math.Clamp(MathF.Floor(Math.Min(a, b)) - 1, first, last + 1);
            last = (int)Math.Clamp(MathF.Ceiling(Math.Max(a, b)) + 1, first - 1, last);
        }
    }
}
