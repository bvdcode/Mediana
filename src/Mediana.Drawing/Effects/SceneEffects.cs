// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Numerics;
namespace Mediana.Drawing
{
    internal static class SceneEffects
    {
        internal static SceneFilterStep Matrix(float[] values) => new(x => SceneFilterKernels.ColorMatrix(values, x.Input, x.Linear, x.Context, x.Overwrite));
        internal static SceneFilterStep Identity() => Matrix([1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0]);
        internal static SceneFilterStep Blur(Vector2 sigma, string edge = "none") => new(x => SceneFilterKernels.Gaussian(x.Input, sigma, x.Units, x.Linear, edge, x.Context, x.Overwrite));
        internal static SceneFilterStep Morphology(float radius, bool dilate) => new(x => SceneFilterKernels.Morphology(new Vector2(radius), dilate, x.Input, x.Units, x.Linear, x.Context, x.Overwrite));
        internal static SceneFilterStep Composite(string mode, Vector4 coefficients = default) => new(x => SceneFilterKernels.Composite(mode, coefficients, x.Input, x.Second, x.Linear, x.Context, x.Overwrite)) { SecondInput = "SourceGraphic" };
        internal static SceneFilterStep Blend(ColorBlendMode mode) => new(x => SceneFilterKernels.BlendPixels(mode, x.Input, x.Second, x.Linear, x.Context)) { SecondInput = "SourceGraphic" };
        internal static SceneFilterStep Transfer(Func<float, float> r, Func<float, float> g, Func<float, float> b, Func<float, float> a)
        {
            Func<float, float>[] functions = [r, g, b, a];
            return new(x => SceneFilterKernels.Transfer(functions, x.Input, x.Linear, x.Context, x.Overwrite));
        }
        internal static Func<float, float> Table(float[] table) => x => SceneFilterKernels.TableValue(table, x);
        internal static SceneFilterStep Shadow(Vector2 sigma, Vector2 offset, Rgba32 color, float opacity) => new(x => SceneFilterKernels.DropShadow(sigma, offset, color, opacity, x.Input, x.Units, x.Linear, x.Context));
        internal static SceneFilterStep Noise(Vector2 frequency, int octaves, long seed) => new(x => SceneFilterKernels.Turbulence(frequency, octaves, seed, true, false, x.Source, x.Matrix, x.Region, x.Linear, x.Context));
        internal static SceneFilterStep Image(SceneNode node) => new(x =>
        {
            PixelBuffer image = x.Context.CreateBuffer(x.Source.Width, x.Source.Height);
            node.Draw(new RasterCanvas(image), x.Units, x.Context);
            return image;
        });
    }
}
