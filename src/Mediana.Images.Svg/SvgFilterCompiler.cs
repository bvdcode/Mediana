// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Xml.Linq;

namespace Mediana.Images.Svg
{
    internal static partial class SvgFilterCompiler
    {
        internal static readonly HashSet<string> Operations = new("feGaussianBlur feOffset feColorMatrix feComponentTransfer feComposite feBlend feMerge feFlood feTile feMorphology feConvolveMatrix feDisplacementMap feTurbulence feDiffuseLighting feSpecularLighting feDropShadow feImage".Split(' '));
        internal static SceneFilter Compile(SvgRegion region, bool primitiveBox, XElement[] primitives, bool linear,
            IReadOnlyDictionary<XElement, SceneNode>? images = null, bool unbounded = false)
        {
            SceneFilterStep[] steps = primitives.Select(p => new SceneFilterStep(x => Execute(p, x.Input, x.Second,
                x.Resolve, x.Source, x.Units, x.Matrix, x.Region, x.InputRegion, x.Linear, x.Context, x.Overwrite, images?.GetValueOrDefault(p)))
            {
                Input = Text(p, "in"),
                SecondInput = p.Name.LocalName is "feComposite" or "feBlend" or "feDisplacementMap" ? Text(p, "in2", "SourceGraphic") : null,
                Result = Text(p, "result"),
                Linear = Text(p, "color-interpolation-filters", linear ? "linearRGB" : "sRGB") != "sRGB",
                Region = box => PrimitiveRegion(region, primitiveBox, p, box),
                AdditionalInputs = p.Descendants().Attributes().Where(a => a.Name.LocalName is "in" or "in2").Select(a => a.Value).ToArray()
            }).ToArray();
            return new SceneFilter(region, primitiveBox, steps, linear) { Unbounded = unbounded };
        }

        private static PixelBuffer Execute(XElement p, PixelBuffer input, PixelBuffer second, Func<string, PixelBuffer> resolve, PixelBuffer source, Matrix3x2 units, Matrix3x2 matrix, (float X, float Y, float Width, float Height) region, (float X, float Y, float Width, float Height) inputRegion, bool linear, SceneRenderContext context, bool overwrite, SceneNode? image) => ColorOperations.Contains(p.Name.LocalName) ? ExecuteColor(p, input, second, resolve, source, linear, context, overwrite) : p.Name.LocalName switch
        {
            "feGaussianBlur" => SceneFilterKernels.Gaussian(input, Pair(p, "stdDeviation"), units, linear, Text(p, "edgeMode", "none"), context, overwrite),
            "feOffset" => Offset(input, Vector2.TransformNormal(new Vector2(Number(p, "dx"), Number(p, "dy")), units), linear, context),
            "feDropShadow" => DropShadow(p, input, units, linear, context),
            "feMorphology" => Morphology(p, input, units, linear, context, overwrite),
            "feConvolveMatrix" => Convolve(p, input, units, linear, context),
            "feDisplacementMap" => Displace(p, input, second, units, linear, context),
            "feTurbulence" => Turbulence(p, source, matrix, region, linear, context),
            "feDiffuseLighting" or "feSpecularLighting" => Lighting(p, input, units, matrix, linear, context),
            "feTile" => Tile(input, inputRegion, matrix, linear, context),
            "feImage" => FilterImage(p, image, source, units, matrix, region, context),
            _ => ScenePixels.Copy(input, context)
        };
        private static readonly HashSet<string> ColorOperations = new("feColorMatrix feComponentTransfer feComposite feBlend feMerge feFlood".Split(' '));
        private static PixelBuffer ExecuteColor(XElement p, PixelBuffer input, PixelBuffer second, Func<string, PixelBuffer> resolve, PixelBuffer source, bool linear, SceneRenderContext context, bool overwrite) => p.Name.LocalName switch
        {
            "feColorMatrix" => ColorMatrix(p, input, linear, context, overwrite),
            "feComponentTransfer" => Transfer(p, input, linear, context, overwrite),
            "feComposite" => Composite(p, input, second, linear, context, overwrite),
            "feBlend" => BlendPixels(p, input, second, linear, context),
            "feMerge" => Merge(p, input, resolve, source, context),
            "feFlood" => Flood(p, source, context),
            _ => ScenePixels.Copy(input, context)
        };
        private static (float X, float Y, float Width, float Height) PrimitiveRegion(SvgRegion Region, bool PrimitiveBox, XElement primitive, (float X, float Y, float Width, float Height) box)
        {
            (float X, float Y, float Width, float Height) region = Region.Path(box).GetBounds();
            float Get(string name, float fallback, float size, float origin, float viewport) => primitive.Attribute(name) is { } a ? PrimitiveBox ? (SvgLength.Parse(a.Value, 1) * size) + origin : SvgLength.Parse(a.Value, viewport) : fallback;
            return (Get("x", region.X, box.Width, box.X, Region.ViewWidth), Get("y", region.Y, box.Height, box.Y, Region.ViewHeight), Get("width", region.Width, box.Width, 0, Region.ViewWidth), Get("height", region.Height, box.Height, 0, Region.ViewHeight));
        }







        private static PixelBuffer FilterImage(XElement p, SceneNode? node, PixelBuffer source, Matrix3x2 units, Matrix3x2 matrix, (float X, float Y, float Width, float Height) region, SceneRenderContext context)
        {
            PixelBuffer? result = context.CreateBuffer(source.Width, source.Height);
            if (node is null || region.Width <= 0 || region.Height <= 0)
            {
                return result;
            }

            RasterCanvas? canvas = new RasterCanvas(result);
            if (node.Image is not null)
            {
                Matrix3x2 viewport = SvgRenderer.Viewport((0, 0, node.Image.Format.Width, node.Image.Format.Height), region.Width, region.Height, Text(p, "preserveAspectRatio", "xMidYMid meet")) * Matrix3x2.CreateTranslation(region.X, region.Y);
                canvas.DrawImage(node.Image, viewport * matrix, cancellationToken: context.Token);
            }
            else
            {
                node.Draw(canvas, units, context);
            }

            return result;
        }

        private static string Text(XElement p, string name, string fallback = "") => (string?)p.Attribute(name) ?? fallback;
        private static float Number(XElement p, string name, float fallback = 0) => p.Attribute(name) is { } a ? SvgLength.Parse(a.Value) : fallback;
        private static Vector2 Pair(XElement p, string name, float fallback = 0)
        {
            float[] values = p.Attribute(name) is { } a ? SvgNumbers.List(a.Value) : new[]
            {
                fallback
            };
            if (values.Length is < 1 or > 2)
            {
                throw new InvalidDataException("Expected one or two SVG filter numbers.");
            }

            return new Vector2(values[0], values.Length == 2 ? values[1] : values[0]);
        }

        private static readonly HashSet<string> CssTransferNames = new("brightness contrast opacity invert".Split(' '));

        internal static XElement? CssOperation(string name, string value)
        {
            if (!new[]
            {
                "blur",
                "hue-rotate",
                "saturate",
                "brightness",
                "contrast",
                "opacity",
                "invert",
                "grayscale",
                "sepia"
            }.Contains(name))
            {
                return null;
            }

            if (value.Length == 0)
            {
                value = name is "blur" or "hue-rotate" ? "0" : "1";
            }

            if (name == "blur")
            {
                return new XElement("feGaussianBlur", new XAttribute("stdDeviation", SvgLength.Parse(value)));
            }

            if (name == "hue-rotate")
            {
                return new XElement("feColorMatrix", new XAttribute("type", "hueRotate"), new XAttribute("values", CssAngle(value)));
            }

            float n = SvgLength.Parse(value);
            if (name == "saturate")
            {
                return new XElement("feColorMatrix", new XAttribute("type", "saturate"), new XAttribute("values", n));
            }

            if (CssTransferNames.Contains(name))
            {
                float slope = name == "invert" ? 1 - (2 * n) : n;
                float intercept = name == "contrast" ? (0.5f - (0.5f * n)) : name == "invert" ? n : 0;
                return new XElement("feComponentTransfer", (name == "opacity" ? new[] { 'A' } : new[] { 'R', 'G', 'B' }).Select(c => new XElement("feFunc" + c, new XAttribute("type", "linear"), new XAttribute("slope", slope), new XAttribute("intercept", intercept))));
            }

            return CssColorOperation(name, n);
        }

        private static float CssAngle(string value)
        {
            if (value.EndsWith("deg", StringComparison.Ordinal))
            {
                return SvgLength.Parse(value[..^3]);
            }

            if (value.EndsWith("rad", StringComparison.Ordinal))
            {
                return (SvgLength.Parse(value[..^3]) * 180) / MathF.PI;
            }

            if (value.EndsWith("turn", StringComparison.Ordinal))
            {
                return SvgLength.Parse(value[..^4]) * 360;
            }

            return SvgLength.Parse(value);
        }

        private static XElement? CssColorOperation(string name, float n)
        {
            if (name is "grayscale" or "sepia")
            {
                float[] m = name == "sepia" ? new[]
                {
                    .393f,
                    .769f,
                    .189f,
                    .349f,
                    .686f,
                    .168f,
                    .272f,
                    .534f,
                    .131f
                }

                : new[]
                {
                    .2126f,
                    .7152f,
                    .0722f,
                    .2126f,
                    .7152f,
                    .0722f,
                    .2126f,
                    .7152f,
                    .0722f
                };
                float[]? values = new float[20];
                for (int i = 0; i < 3; i++)
                {
                    for (int j = 0; j < 3; j++)
                    {
                        values[(i * 5) + j] = (m[(i * 3) + j] * n) + (i == j ? 1 - n : 0);
                    }
                }

                values[18] = 1;
                return new XElement("feColorMatrix", new XAttribute("values", string.Join(" ", values.Select(v => v.ToString("R", CultureInfo.InvariantCulture)))));
            }

            return null;
        }
    }
}
