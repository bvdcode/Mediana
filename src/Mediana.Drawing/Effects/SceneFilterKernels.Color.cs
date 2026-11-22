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

            Vector4 red = new(m[0], m[5], m[10], m[15]), green = new(m[1], m[6], m[11], m[16]),
                blue = new(m[2], m[7], m[12], m[17]), alpha = new(m[3], m[8], m[13], m[18]),
                bias = new(m[4], m[9], m[14], m[19]);
            Vector4 Convert(Vector4 color) => Vector4.Clamp(
                red * color.X + green * color.Y + blue * color.Z + alpha * color.W + bias, Vector4.Zero, Vector4.One);
            Span<int> channels = stackalloc int[4];
            if (SceneChannelLookup.TryGetSourceChannels(m, channels))
            {
                Span<byte> lookup = stackalloc byte[256 * 4];
                for (int value = 0; value < 256; value++)
                {
                    context.Token.ThrowIfCancellationRequested();
                    Span<byte> pixel = lookup.Slice(value * 4, 4);
                    pixel.Fill((byte)value);
                    ScenePixels.Write(pixel, 0, Convert(ScenePixels.Read(pixel, 0, linear, false)), linear, false);
                }
                return SceneChannelLookup.Apply(input, lookup, channels, context, overwrite);
            }
            PixelBuffer output = overwrite ? input : context.CreateBuffer(input.Width, input.Height);
            ReadOnlySpan<byte> source = input.Pixels.Span;
            Span<byte> destination = output.Pixels.Span;
            for (int y = 0; y < input.Height; y++)
            {
                context.Token.ThrowIfCancellationRequested();
                int end = (y + 1) * input.Width * 4;
                for (int i = y * input.Width * 4; i < end; i += 4)
                { ScenePixels.Write(destination, i, Convert(ScenePixels.Read(source, i, linear, false)), linear, false); }
            }
            return output;
        }
        internal static PixelBuffer Transfer(Func<float, float>[] functions, PixelBuffer input, bool linear, SceneRenderContext context, bool overwrite)
        {
            Span<byte> lookup = stackalloc byte[256 * 4];
            for (int value = 0; value < 256; value++)
            {
                context.Token.ThrowIfCancellationRequested();
                Span<byte> pixel = lookup.Slice(value * 4, 4);
                pixel.Fill((byte)value);
                Vector4 c = ScenePixels.Read(pixel, 0, linear, false);
                ScenePixels.Write(pixel, 0, new Vector4(functions[0](c.X), functions[1](c.Y), functions[2](c.Z), functions[3](c.W)), linear, false);
            }
            return SceneChannelLookup.Apply(input, lookup, [0, 1, 2, 3], context, overwrite);
        }
        internal static PixelBuffer Composite(string operation, Vector4 coefficients, PixelBuffer input, PixelBuffer second, bool linear, SceneRenderContext context, bool overwrite)
        {
            PixelBuffer output = overwrite ? input : context.CreateBuffer(input.Width, input.Height);
            ReadOnlySpan<byte> firstPixels = input.Pixels.Span, secondPixels = second.Pixels.Span;
            Span<byte> destination = output.Pixels.Span;
            for (int y = 0; y < input.Height; y++)
            {
                context.Token.ThrowIfCancellationRequested();
                int end = (y + 1) * input.Width * 4;
                for (int i = y * input.Width * 4; i < end; i += 4)
                {
                    Vector4 a = ScenePixels.Read(firstPixels, i, linear), b = ScenePixels.Read(secondPixels, i, linear);
                    Vector4 result = operation switch
                    {
                        "in" => a * b.W,
                        "out" => a * (1 - b.W),
                        "atop" => (a * b.W) + (b * (1 - a.W)),
                        "xor" => (a * (1 - b.W)) + (b * (1 - a.W)),
                        "arithmetic" => Arithmetic(a, b, coefficients),
                        "lighter" => Vector4.Min(a + b, Vector4.One),
                        _ => a + (b * (1 - a.W))
                    };
                    ScenePixels.Write(destination, i, result, linear);
                }
            }
            return output;
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
            PixelBuffer alpha = ScenePixels.ExtractAlpha(input, context);
            PixelBuffer blur = Gaussian(alpha, sigma, units, linear, "none", context, overwrite: true);
            PixelBuffer offset = Offset(blur, Vector2.TransformNormal(delta, units), linear, context);
            context.Release(blur);
            PixelBuffer result = Transfer([_ => color.R / 255f, _ => color.G / 255f, _ => color.B / 255f,
                alpha => alpha * opacity * (color.A / 255f)], offset, false, context, overwrite: true);
            new RasterCanvas(result).DrawImage(input, Matrix3x2.Identity, cancellationToken: context.Token);
            return result;
        }
        internal static float TableValue(float[] table, float x)
        {
            float position = x * (table.Length - 1);
            int i = Math.Min(table.Length - 2, (int)position);
            return table[i] + ((table[i + 1] - table[i]) * (position - i));
        }
    }
}
