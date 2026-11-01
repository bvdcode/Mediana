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
        internal static PixelBuffer ColorMatrix(float[] m, PixelBuffer input, bool linear, SceneRenderContext context, bool overwrite)
        {
            if (m.Length != 20)
            {
                throw new InvalidDataException("Drawing color matrix requires 20 values.");
            }

            return ScenePixels.Map(input, (x, y) =>
            {
                Vector4 c = ScenePixels.At(input, x, y, linear, premultiplied: false);
                float Row(int i) => (c.X * m[i]) + (c.Y * m[i + 1]) + (c.Z * m[i + 2]) + (c.W * m[i + 3]) + m[i + 4];
                return Vector4.Clamp(new Vector4(Row(0), Row(5), Row(10), Row(15)), Vector4.Zero, Vector4.One);
            }, linear, context, false, destination: overwrite ? input : null);
        }
        internal static PixelBuffer Transfer(Func<float, float>[] functions, PixelBuffer input, bool linear, SceneRenderContext context, bool overwrite)
        {
            return ScenePixels.Map(input, (x, y) =>
            {
                Vector4 c = ScenePixels.At(input, x, y, linear, premultiplied: false);
                return new Vector4(functions[0](c.X), functions[1](c.Y), functions[2](c.Z), functions[3](c.W));
            }, linear, context, false, destination: overwrite ? input : null);
        }
        internal static PixelBuffer Composite(string operation, Vector4 coefficients, PixelBuffer input, PixelBuffer second, bool linear, SceneRenderContext context, bool overwrite)
        {
            return ScenePixels.Map(input, (x, y) =>
            {
                Vector4 a = ScenePixels.At(input, x, y, linear), b = ScenePixels.At(second, x, y, linear);
                return operation switch
                {
                    "in" => a * b.W,
                    "out" => a * (1 - b.W),
                    "atop" => (a * b.W) + (b * (1 - a.W)),
                    "xor" => (a * (1 - b.W)) + (b * (1 - a.W)),
                    "arithmetic" => Arithmetic(a, b, coefficients),
                    "lighter" => Vector4.Min(a + b, Vector4.One),
                    _ => a + (b * (1 - a.W))
                };
            }, linear, context, destination: overwrite ? input : null);
        }
        private static Vector4 Arithmetic(Vector4 a, Vector4 b, Vector4 k)
        {
            Vector4 c = Vector4.Clamp((k.X * a * b) + (k.Y * a) + (k.Z * b) + new Vector4(k.W), Vector4.Zero, Vector4.One);
            return Vector4.Min(c, new Vector4(c.W));
        }
        internal static PixelBuffer BlendPixels(ColorBlendMode mode, PixelBuffer input, PixelBuffer second, bool linear, SceneRenderContext context)
        {
            PixelBuffer? output = ScenePixels.Copy(second, context);
            for (int y = 0; y < input.Height; y++)
            {
                context.Token.ThrowIfCancellationRequested();
                for (int x = 0; x < input.Width; x++)
                {
                    int i = ((y * input.Width) + x) * 4;
                    Rgba32 color = input[x, y];
                    if (linear)
                    {
                        Vector4 a = ScenePixels.At(input, x, y, true, premultiplied: false), b = ScenePixels.At(second, x, y, true, premultiplied: false);
                        ScenePixels.Write(output.Pixels.Span, i, b, false, false);
                        color = new Rgba32(PixelCompositor.Byte(a.X), PixelCompositor.Byte(a.Y), PixelCompositor.Byte(a.Z), PixelCompositor.Byte(a.W));
                    }

                    PixelCompositor.Blend(output.Pixels.Span, i, color, mode);
                    if (linear)
                    {
                        Vector4 c = ScenePixels.Read(output.Pixels.Span, i, false, false);
                        ScenePixels.Write(output.Pixels.Span, i, c, true, false);
                    }
                }
            }

            return output;
        }
        internal static PixelBuffer DropShadow(Vector2 sigma, Vector2 delta, Rgba32 color, float opacity, PixelBuffer input, Matrix3x2 units, bool linear, SceneRenderContext context)
        {
            PixelBuffer alpha = ScenePixels.Map(input, (x, y) => new Vector4(0, 0, 0, input[x, y].A / 255f), false, context);
            PixelBuffer blur = Gaussian(alpha, sigma, units, linear, "none", context, overwrite: true);
            PixelBuffer offset = Offset(blur, Vector2.TransformNormal(delta, units), linear, context);
            context.Release(blur);
            PixelBuffer result = ScenePixels.Map(input, (x, y) => new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, (offset[x, y].A / 255f) * opacity * (color.A / 255f)), false, context, false);
            context.Release(offset);
            new RasterCanvas(result).DrawImage(input, Matrix3x2.Identity, cancellationToken: context.Token);
            return result;
        }
        private static PixelBuffer Offset(PixelBuffer input, Vector2 delta, bool linear, SceneRenderContext context) => ScenePixels.Map(input, (x, y) => ScenePixels.Sample(input, x - delta.X, y - delta.Y, linear), linear, context);
        internal static float TableValue(float[] table, float x)
        {
            float position = x * (table.Length - 1);
            int i = Math.Min(table.Length - 2, (int)position);
            return table[i] + ((table[i + 1] - table[i]) * (position - i));
        }
    }
}
