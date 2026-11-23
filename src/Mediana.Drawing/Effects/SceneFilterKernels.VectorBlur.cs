// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Mediana.Drawing
{
    internal static partial class SceneFilterKernels
    {
        private static int GaussianVectorizedLine(PixelBuffer result, Vector4[] samples, float[] kernel, int line,
            int start, int end, int first, int last, bool horizontal, bool reverse, bool linear, SceneRenderContext context)
        {
            ReadOnlySpan<float> channels = MemoryMarshal.Cast<Vector4, float>(samples);
            ref float origin = ref MemoryMarshal.GetReference(channels);
            Span<Vector<float>> results = stackalloc Vector<float>[4];
            Span<Vector4> pixels = MemoryMarshal.Cast<Vector<float>, Vector4>(results);
            int block = pixels.Length;
            int x = start;
            for (; x <= end - (block - 1); x += block)
            {
                context.Token.ThrowIfCancellationRequested();
                Vector<float> a = Vector<float>.Zero, b = Vector<float>.Zero;
                Vector<float> c = Vector<float>.Zero, d = Vector<float>.Zero;
                int kernelStart = Math.Max(0, first - (x + block - 1)), kernelEnd = Math.Min(kernel.Length - 1, last - x);
                for (int i = kernelStart; i <= kernelEnd; i++)
                {
                    Vector<float> weight = new(kernel[i]);
                    nuint offset = (nuint)((x + i) * 4);
                    a += Vector.LoadUnsafe(ref origin, offset) * weight;
                    b += Vector.LoadUnsafe(ref origin, offset + (nuint)Vector<float>.Count) * weight;
                    c += Vector.LoadUnsafe(ref origin, offset + (nuint)(2 * Vector<float>.Count)) * weight;
                    d += Vector.LoadUnsafe(ref origin, offset + (nuint)(3 * Vector<float>.Count)) * weight;
                }

                results[0] = a;
                results[1] = b;
                results[2] = c;
                results[3] = d;
                for (int pixel = 0; pixel < block; pixel++)
                {
                    WriteAxisPixel(result, x + pixel, line, horizontal, reverse, pixels[pixel], linear);
                }
            }
            return x;
        }
    }
}
