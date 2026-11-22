// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using Mediana.Drawing;

namespace Mediana.Tests
{
    internal static class SceneFilterPixelTestInput
    {
        internal static void VerifyMatrices(bool linear, bool overwrite)
        {
            float[][] matrices =
            [
                [1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0],
                [0, 0, 0, 1, 0, 0, 0, -1, 0, 1, 0, .37f, 0, 0, .2f, .6f, 0, 0, 0, .1f],
                [0, 0, 0, 0, .53f, 0, 0, 0, 0, .25f, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0],
                [.2f, .7f, .1f, 0, 0, -.5f, .3f, .6f, .2f, .1f, .2f, .2f, -.3f, .7f, -.1f, .2f, .5f, .1f, .2f, .05f]
            ];
            foreach (float[] matrix in matrices)
            {
                using PixelBuffer input = Input();
                byte[] expected = input.Pixels.ToArray();
                for (int i = 0; i < expected.Length; i += 4)
                {
                    Vector4 c = ScenePixels.Read(expected, i, linear, false);
                    Vector4 transformed = new(
                        matrix[0] * c.X + matrix[1] * c.Y + matrix[2] * c.Z + matrix[3] * c.W + matrix[4],
                        matrix[5] * c.X + matrix[6] * c.Y + matrix[7] * c.Z + matrix[8] * c.W + matrix[9],
                        matrix[10] * c.X + matrix[11] * c.Y + matrix[12] * c.Z + matrix[13] * c.W + matrix[14],
                        matrix[15] * c.X + matrix[16] * c.Y + matrix[17] * c.Z + matrix[18] * c.W + matrix[19]);
                    ScenePixels.Write(expected, i, Vector4.Clamp(transformed, Vector4.Zero, Vector4.One), linear, false);
                }
                using SceneRenderContext context = new(new(), default);
                using SceneBufferScope scope = context.BeginBuffers();
                PixelBuffer actual = SceneFilterKernels.ColorMatrix(matrix, input, linear, context, overwrite);
                Require(actual.Pixels.Span.SequenceEqual(expected));
                Require(ReferenceEquals(actual, input) == overwrite);
            }
        }

        internal static void VerifyTransfer(bool linear, bool overwrite)
        {
            Func<float, float>[] functions = [x => 1 - x, x => 1.3f * MathF.Pow(x, 2.2f) - .1f,
                x => SceneFilterKernels.TableValue([0, .7f, .2f, 1], x), x => x > .5f ? .75f : .25f];
            using PixelBuffer input = Input();
            byte[] expected = input.Pixels.ToArray();
            for (int i = 0; i < expected.Length; i += 4)
            {
                Vector4 c = ScenePixels.Read(expected, i, linear, false);
                ScenePixels.Write(expected, i, new(functions[0](c.X), functions[1](c.Y), functions[2](c.Z), functions[3](c.W)), linear, false);
            }
            using SceneRenderContext context = new(new(), default);
            using SceneBufferScope scope = context.BeginBuffers();
            PixelBuffer actual = SceneFilterKernels.Transfer(functions, input, linear, context, overwrite);
            Require(actual.Pixels.Span.SequenceEqual(expected));
            Require(ReferenceEquals(actual, input) == overwrite);
        }

        internal static void VerifyOffsets(bool linear)
        {
            Vector2[] offsets = [Vector2.Zero, new(3, -1), new(-4, 2), new(.333f, -.723f), new(-2.6f, 1.2f), new(300, -300)];
            using PixelBuffer input = Input();
            for (int y = 0; y < input.Height; y++)
            {
                for (int x = 0; x < input.Width; x++)
                {
                    if (x < 31 || x > 150 || y == 0 || y == input.Height - 1)
                    { input[x, y] = new(127, 255, 31, 0); }
                }
            }
            foreach (Vector2 offset in offsets)
            {
                using SceneRenderContext context = new(new(), default);
                using SceneBufferScope scope = context.BeginBuffers();
                PixelBuffer expected = ScenePixels.Map(input,
                    (x, y) => ScenePixels.Sample(input, x - offset.X, y - offset.Y, linear), linear, context);
                PixelBuffer recycled = context.CreateBuffer(input.Width, input.Height);
                recycled.Clear(new(255, 255, 255));
                context.Release(recycled);
                PixelBuffer actual = SceneFilterKernels.Offset(input, offset, linear, context);
                Require(actual.Pixels.Span.SequenceEqual(expected.Pixels.Span));
            }
        }

        internal static void VerifyCancellation()
        {
            using PixelBuffer input = Input();
            using SceneRenderContext context = new(new(), new(true));
            using SceneBufferScope scope = context.BeginBuffers();
            RequireCanceled(() => SceneFilterKernels.Offset(input, Vector2.Zero, false, context));
            RequireCanceled(() => SceneFilterKernels.ColorMatrix(new float[20], input, false, context, true));
            RequireCanceled(() => SceneFilterKernels.Transfer([x => x, x => x, x => x, x => x], input, false, context, true));
        }

        private static PixelBuffer Input()
        {
            PixelBuffer pixels = new(256, 5);
            for (int y = 0; y < pixels.Height; y++)
            {
                for (int x = 0; x < pixels.Width; x++)
                {
                    pixels[x, y] = new((byte)x, (byte)(255 - x), (byte)((x * 73 + y * 31) % 256), (byte)((x * 131 + y * 23) % 256));
                }
            }
            return pixels;
        }

        private static void RequireCanceled(Action action)
        {
            try { action(); }
            catch (OperationCanceledException) { return; }
            throw new InvalidDataException("Scene filter ignored cancellation.");
        }

        private static void Require(bool condition)
        {
            if (!condition) { throw new InvalidDataException("Scene filter differs from scalar RGBA arithmetic."); }
        }
    }
}
