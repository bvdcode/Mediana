// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.InteropServices;

namespace Mediana.Drawing
{
    internal static class ScanlineCoverageIntegrator
    {
        internal static void Add(Span<float> coverage, int origin, double leftTop, double leftBottom, double rightTop, double rightBottom, float height)
        {
            int first = (int)Math.Clamp(Math.Floor(Math.Min(leftTop, leftBottom)), origin, origin + coverage.Length);
            int last = (int)Math.Clamp(Math.Ceiling(Math.Max(rightTop, rightBottom)), origin, origin + coverage.Length);
            int solidStart = (int)Math.Clamp(Math.Ceiling(Math.Max(leftTop, leftBottom)), first, last);
            int solidEnd = (int)Math.Clamp(Math.Floor(Math.Min(rightTop, rightBottom)), solidStart, last);
            for (int x = first; x < solidStart; x++) { coverage[x - origin] += Area(x, leftTop, leftBottom, rightTop, rightBottom) * height; }
            Span<float> interior = coverage.Slice(solidStart - origin, solidEnd - solidStart);
            Span<Vector<float>> vectors = MemoryMarshal.Cast<float, Vector<float>>(interior);
            Vector<float> increment = new(height);
            for (int i = 0; i < vectors.Length; i++) { vectors[i] += increment; }
            for (int i = vectors.Length * Vector<float>.Count; i < interior.Length; i++) { interior[i] += height; }
            for (int x = solidEnd; x < last; x++) { coverage[x - origin] += Area(x, leftTop, leftBottom, rightTop, rightBottom) * height; }

        }

        private static float Area(int x, double leftTop, double leftBottom, double rightTop, double rightBottom) =>
            (float)(Integral(rightTop - x, rightBottom - x) - Integral(leftTop - x, leftBottom - x));

        private static double Integral(double first, double last)
        {
            double low = Math.Min(first, last), high = Math.Max(first, last);
            if (high <= 0) { return 0; }
            if (low >= 1) { return 1; }
            if (low == high) { return low; }
            double start = Math.Max(0, low), end = Math.Min(1, high);
            return (Math.Max(0, high - 1) + .5 * (end - start) * (end + start)) / (high - low);
        }
    }
}
