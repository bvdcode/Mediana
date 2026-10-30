// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Xml.Linq;

namespace Mediana.Images.Svg
{
    internal static partial class SvgFilterCompiler
    {
        private static PixelBuffer ColorMatrix(XElement p, PixelBuffer input, bool linear, SceneRenderContext context, bool overwrite)
        {
            string type = Text(p, "type", "matrix");
            float[] numbers = SvgNumbers.List(Text(p, "values", ""));
            float value = numbers.Length > 0 ? numbers[0] : 1;
            float[] m = type switch
            {
                "saturate" => new[]
                {
                    .213f + (.787f * value),
                    .715f - (.715f * value),
                    .072f - (.072f * value),
                    0,
                    0,
                    .213f - (.213f * value),
                    .715f + (.285f * value),
                    .072f - (.072f * value),
                    0,
                    0,
                    .213f - (.213f * value),
                    .715f - (.715f * value),
                    .072f + (.928f * value),
                    0,
                    0,
                    0,
                    0,
                    0,
                    1,
                    0
                },
                "hueRotate" => HueMatrix(value),
                "luminanceToAlpha" => new[]
                {
                    0f,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    .2125f,
                    .7154f,
                    .0721f,
                    0,
                    0
                },
                _ => numbers.Length == 0 ? new[]
                {
                    1f,
                    0,
                    0,
                    0,
                    0,
                    0,
                    1,
                    0,
                    0,
                    0,
                    0,
                    0,
                    1,
                    0,
                    0,
                    0,
                    0,
                    0,
                    1,
                    0
                }

                : numbers
            };
            return SceneFilterKernels.ColorMatrix(m, input, linear, context, overwrite);
        }

        private static float[] HueMatrix(float angle)
        {
            float c = MathF.Cos((angle * MathF.PI) / 180), s = MathF.Sin((angle * MathF.PI) / 180);
            return new[]
            {
                (.213f + (.787f * c)) - (.213f * s),
                .715f - (.715f * c) - (.715f * s),
                (.072f - (.072f * c)) + (.928f * s),
                0,
                0,
                (.213f - (.213f * c)) + (.143f * s),
                .715f + (.285f * c) + (.140f * s),
                .072f - (.072f * c) - (.283f * s),
                0,
                0,
                .213f - (.213f * c) - (.787f * s),
                (.715f - (.715f * c)) + (.715f * s),
                .072f + (.928f * c) + (.072f * s),
                0,
                0,
                0,
                0,
                0,
                1,
                0
            };
        }

        private static PixelBuffer Transfer(XElement p, PixelBuffer input, bool linear, SceneRenderContext context, bool overwrite)
        {
            Func<float, float>[] functions = "RGBA".Select(c => TransferFunction(p.Elements().LastOrDefault(e => e.Name.LocalName == "feFunc" + c))).ToArray();
            return SceneFilterKernels.Transfer(functions, input, linear, context, overwrite);
        }

        private static Func<float, float> TransferFunction(XElement? function)
        {
            if (function is null)
            {
                return x => x;
            }

            float[] table = SvgNumbers.List(Text(function, "tableValues"));
            string type = Text(function, "type", "identity");
            float slope = Number(function, "slope", 1), intercept = Number(function, "intercept");
            float amplitude = Number(function, "amplitude", 1), exponent = Number(function, "exponent", 1), offset = Number(function, "offset");
            return type switch
            {
                "linear" => x => (slope * x) + intercept,
                "gamma" => x => (amplitude * MathF.Pow(x, exponent)) + offset,
                "discrete" when table.Length > 0 => x => table[Math.Min(table.Length - 1, (int)(x * table.Length))],
                "table" when table.Length > 1 => x => SceneFilterKernels.TableValue(table, x),
                "table" when table.Length == 1 => _ => table[0],
                _ => x => x
            };
        }



        private static PixelBuffer Composite(XElement p, PixelBuffer input, PixelBuffer second, bool linear, SceneRenderContext context, bool overwrite)
        {
            string operation = Text(p, "operator", "over");
            Vector4 coefficients = new Vector4(Number(p, "k1"), Number(p, "k2"), Number(p, "k3"), Number(p, "k4"));
            return SceneFilterKernels.Composite(operation, coefficients, input, second, linear, context, overwrite);
        }



        private static PixelBuffer BlendPixels(XElement p, PixelBuffer input, PixelBuffer second, bool linear, SceneRenderContext context)
        {
            string name = Text(p, "mode", "normal").Replace("-", "");
            if (!Enum.TryParse(name, true, out ColorBlendMode mode))
            {
                throw new NotSupportedException("SVG filter blend mode: " + name);
            }

            return SceneFilterKernels.BlendPixels(mode, input, second, linear, context);
        }

        private static PixelBuffer Merge(XElement p, PixelBuffer previous, Func<string, PixelBuffer> resolve, PixelBuffer source, SceneRenderContext context)
        {
            PixelBuffer? output = context.CreateBuffer(source.Width, source.Height);
            RasterCanvas? canvas = new RasterCanvas(output);
            foreach (XElement child in p.Elements().Where(e => e.Name.LocalName == "feMergeNode"))
            {
                canvas.DrawImage(resolve(Text(child, "in")), Matrix3x2.Identity, cancellationToken: context.Token);
            }

            return output;
        }

        private static PixelBuffer Flood(XElement p, PixelBuffer source, SceneRenderContext context)
        {
            Rgba32 color = SvgColor.Parse(Text(p, "flood-color", "black"));
            PixelBuffer? result = context.CreateBuffer(source.Width, source.Height);
            result.Clear(color with { A = (byte)MathF.Round(color.A * Math.Clamp(Number(p, "flood-opacity", 1), 0, 1)) });
            return result;
        }

        private static PixelBuffer DropShadow(XElement p, PixelBuffer input, Matrix3x2 units, bool linear, SceneRenderContext context)
            => SceneFilterKernels.DropShadow(Pair(p, "stdDeviation", 2), new Vector2(Number(p, "dx", 2), Number(p, "dy", 2)), SvgColor.Parse(Text(p, "flood-color", "black")), Number(p, "flood-opacity", 1), input, units, linear, context);

    }
}
