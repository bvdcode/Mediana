// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Drawing
{
    internal static partial class SceneFilterKernels
    {
        internal static PixelBuffer Offset(PixelBuffer input, Vector2 delta, bool linear, SceneRenderContext context)
        {
            PixelBuffer output = context.CreateBuffer(input.Width, input.Height);
            ScenePixelRegion source = ScenePixelRegion.AlphaBounds(input, context.Token);
            if (source.Empty) { return output; }
            int left = (int)Math.Clamp(MathF.Floor(source.Left + delta.X) - 1, 0, input.Width);
            int right = (int)Math.Clamp(MathF.Ceiling(source.Right + delta.X) + 1, 0, input.Width);
            int top = (int)Math.Clamp(MathF.Floor(source.Top + delta.Y) - 1, 0, input.Height);
            int bottom = (int)Math.Clamp(MathF.Ceiling(source.Bottom + delta.Y) + 1, 0, input.Height);
            ScenePixelSampler samples = new(input, source, linear, "none");
            Span<byte> destination = output.Pixels.Span;
            for (int y = top; y < bottom; y++)
            {
                context.Token.ThrowIfCancellationRequested();
                for (int x = left; x < right; x++)
                {
                    ScenePixels.Write(destination, (y * input.Width + x) * 4,
                        samples.Sample(x - delta.X, y - delta.Y), linear);
                }
            }
            return output;
        }
    }
}
