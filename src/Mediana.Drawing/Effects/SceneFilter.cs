// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
namespace Mediana.Drawing
{
    internal partial record SceneFilter(SceneRegion Region, bool PrimitiveBox, SceneFilterStep[] Steps, bool Linear) : SceneEffect
    {
        internal bool Unbounded { get; init; }
        internal override PixelBuffer Apply(PixelBuffer source, (float X, float Y, float Width, float Height) box, Matrix3x2 matrix, SceneRenderContext context, PixelBuffer background, ScenePaint? fill, ScenePaint? stroke, float fillOpacity, float strokeOpacity)
        {
            using SceneBufferScope buffers = context.BeginBuffers();
            buffers.Adopt(source);
            Dictionary<string, int> lastUses = LastUses();
            var results = new Dictionary<string, PixelBuffer>(StringComparer.Ordinal)
            {
                ["SourceGraphic"] = source
            };
            PrepareInputs(results, background, fill, stroke, fillOpacity, strokeOpacity, box, matrix, source, context);
            var regions = new Dictionary<PixelBuffer, (float X, float Y, float Width, float Height)>
            {
                [source] = Region.Path(box).GetBounds(context.Token)
            };
            PixelBuffer previous = source;
            Matrix3x2 units = PrimitiveBox ? Matrix3x2.CreateScale(box.Width, box.Height) * Matrix3x2.CreateTranslation(box.X, box.Y) * matrix : matrix;
            for (int index = 0; index < Steps.Length; index++)
            {
                SceneFilterStep primitive = Steps[index];
                context.Token.ThrowIfCancellationRequested();
                PixelBuffer input = Input(primitive.Input, previous, results, source, context);
                PixelBuffer second = input;
                if (primitive.SecondInput is not null)
                {
                    second = Input(primitive.SecondInput!, source, results, source, context);
                }
                bool linear = primitive.Linear ?? Linear;
                (float X, float Y, float Width, float Height) region = primitive.Region?.Invoke(box) ?? Region.Path(box).GetBounds(context.Token);
                bool overwrite = CanOverwrite(input, results, lastUses, index, buffers);
                PixelBuffer Resolve(string name) => Input(name, input, results, source, context);
                previous = primitive.Render(new SceneFilterInput(input, second, source, units, matrix, region,
                    regions.GetValueOrDefault(input, Region.Path(box).GetBounds(context.Token)), linear, context, overwrite, Resolve));
                ClipPrimitive(previous, region, matrix, context);
                regions[previous] = region;
                string name = primitive.Result;
                if (name.Length > 0 && name != "SourceGraphic" && name != "SourceAlpha")
                {
                    results[name] = previous;
                }

                ReleaseResults(results, regions, previous, lastUses, index, buffers);
            }

            if (Steps.Length == 0)
            {
                previous = context.CreateBuffer(source.Width, source.Height);
            }

            if (Unbounded)
            {
                return buffers.Transfer(previous);
            }

            Clip(previous, Region.Path(box).GetBounds(context.Token), matrix, context, true);
            return buffers.Transfer(previous);
        }

        private static PixelBuffer Input(string name, PixelBuffer fallback, Dictionary<string, PixelBuffer> results, PixelBuffer source, SceneRenderContext context)
        {
            if (name.Length == 0)
            {
                return fallback;
            }

            if (results.TryGetValue(name, out PixelBuffer? found))
            {
                return found;
            }

            if (name == "SourceAlpha")
            {
                PixelBuffer alpha = ScenePixels.Map(source, (x, y) => new Vector4(0, 0, 0, source[x, y].A / 255f), false, context);
                results[name] = alpha;
                return alpha;
            }

            return context.CreateBuffer(source.Width, source.Height);
        }

        private static void Clip(PixelBuffer image, (float X, float Y, float Width, float Height) region, Matrix3x2 matrix, SceneRenderContext context, bool clearColor = false)
        {
            VectorPath? path = region.Width <= 0 || region.Height <= 0 ? new VectorPath() : new VectorPath().Rectangle(region.X, region.Y, region.Width, region.Height);
            int[] lefts = new int[image.Height], rights = new int[image.Height];
            ScanlineRasterizer.Paint(path, matrix, FillRule.NonZero, image.Width, image.Height, (y, left, right) =>
            {
                lefts[y] = left;
                rights[y] = right;
            }, context.Token);
            Span<byte> pixels = image.Pixels.Span;
            for (int y = 0; y < image.Height; y++)
            {
                context.Token.ThrowIfCancellationRequested();
                int row = (y * image.Width * 4) + 3;
                if (clearColor)
                {
                    pixels.Slice(row - 3, lefts[y] * 4).Clear();
                    pixels.Slice(row - 3 + (rights[y] * 4), (image.Width - rights[y]) * 4).Clear();
                    for (int x = lefts[y]; x < rights[y]; x++)
                    {
                        Rgba32 color = image[x, y];
                        Span<byte> pixel = pixels.Slice(row - 3 + (x * 4), 4);
                        pixel.Clear();
                        PixelCompositor.Blend(pixel, 0, color, ColorBlendMode.Normal);
                    }

                    continue;
                }

                for (int x = 0; x < lefts[y]; x++)
                {
                    pixels[row + (x * 4)] = 0;
                }

                for (int x = rights[y]; x < image.Width; x++)
                {
                    pixels[row + (x * 4)] = 0;
                }
            }
        }
        private void ClipPrimitive(PixelBuffer image, (float X, float Y, float Width, float Height) region, Matrix3x2 matrix, SceneRenderContext context)
        {
            if (!Unbounded)
            {
                Clip(image, region, matrix, context);
            }
        }
        private void PrepareInputs(Dictionary<string, PixelBuffer> results, PixelBuffer background, ScenePaint? fill, ScenePaint? stroke, float fillOpacity, float strokeOpacity, (float X, float Y, float Width, float Height) box, Matrix3x2 matrix, PixelBuffer source, SceneRenderContext context)
        {
            HashSet<string> requested = Steps.SelectMany(step => step.Inputs).ToHashSet(StringComparer.Ordinal);
            if (requested.Contains("BackgroundImage"))
            {
                results["BackgroundImage"] = background;
            }

            if (requested.Contains("BackgroundAlpha"))
            {
                results["BackgroundAlpha"] = ScenePixels.Map(source, (x, y) => new Vector4(0, 0, 0, background[x, y].A / 255f), false, context);
            }

            void Paint(string name, ScenePaint? paint, float opacity)
            {
                if (requested.Contains(name))
                {
                    PixelBuffer? buffer = context.CreateBuffer(source.Width, source.Height);
                    paint?.DrawUnbounded(new RasterCanvas(buffer), box, matrix, opacity, context.Token);
                    results[name] = buffer;
                }
            }

            Paint("FillPaint", fill, fillOpacity);
            Paint("StrokePaint", stroke, strokeOpacity);
        }
    }
}
