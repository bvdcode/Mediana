// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.InteropServices;

namespace Mediana.Codecs.Opus
{
    internal static class CeltFourier
    {
        public static void Transform(Span<float> workspace, ReadOnlySpan<float> rotations)
        {
            Span<Vector2> values = MemoryMarshal.Cast<float, Vector2>(workspace);
            ReadOnlySpan<Vector2> factors = MemoryMarshal.Cast<float, Vector2>(rotations);
            Apply(values, factors, 5, 5);
            Apply(values, factors, 15, 3);
            int stage = 15;
            if ((BitOperations.Log2((uint)(values.Length / stage)) & 1) != 0)
            {
                stage *= 2;
                Apply(values, factors, stage, 2);
            }
            while (stage < values.Length)
            {
                stage *= 4;
                Apply(values, factors, stage, 4);
            }
        }

        private static void Apply(Span<Vector2> values, ReadOnlySpan<Vector2> factors, int stage, int radix)
        {
            int width = stage / radix, rotationStep = CeltTransformPlan.MaximumFourierSize / stage;
            float cosine1 = factors[CeltTransformPlan.MaximumFourierSize / 5].X;
            float cosine2 = factors[2 * CeltTransformPlan.MaximumFourierSize / 5].X;
            float sine1 = -factors[CeltTransformPlan.MaximumFourierSize / 5].Y;
            float sine2 = -factors[2 * CeltTransformPlan.MaximumFourierSize / 5].Y;
            for (int start = 0; start < values.Length; start += stage)
            {
                for (int offset = 0; offset < width; offset++)
                {
                    int at = start + offset, rotation = offset * rotationStep;
                    Vector2 a = values[at], b = values[at + width];
                    if (offset != 0) { b = Rotate(b, factors[rotation]); }
                    if (radix == 2)
                    {
                        values[at] = a + b;
                        values[at + width] = a - b;
                        continue;
                    }
                    Vector2 c = values[at + 2 * width];
                    if (offset != 0) { c = Rotate(c, factors[2 * rotation]); }
                    if (radix == 3)
                    {
                        Vector2 sum = b + c, difference = b - c;
                        Vector2 center = a - .5f * sum;
                        Vector2 turn = new Vector2(difference.Y, -difference.X) * .8660254037844386f;
                        values[at] = a + sum;
                        values[at + width] = center + turn;
                        values[at + 2 * width] = center - turn;
                        continue;
                    }
                    Vector2 d = values[at + 3 * width];
                    if (offset != 0) { d = Rotate(d, factors[3 * rotation]); }
                    if (radix == 4)
                    {
                        Vector2 even = a + c, odd = b + d, difference = a - c, side = b - d;
                        Vector2 turn = new(side.Y, -side.X);
                        values[at] = even + odd;
                        values[at + width] = difference + turn;
                        values[at + 2 * width] = even - odd;
                        values[at + 3 * width] = difference - turn;
                        continue;
                    }
                    Vector2 e = values[at + 4 * width];
                    if (offset != 0) { e = Rotate(e, factors[4 * rotation]); }
                    Vector2 outerSum = b + e, innerSum = c + d, outerDifference = b - e, innerDifference = c - d;
                    Vector2 center1 = a + cosine1 * outerSum + cosine2 * innerSum;
                    Vector2 center2 = a + cosine2 * outerSum + cosine1 * innerSum;
                    Vector2 side1 = sine1 * outerDifference + sine2 * innerDifference;
                    Vector2 side2 = sine2 * outerDifference - sine1 * innerDifference;
                    Vector2 turn1 = new(side1.Y, -side1.X), turn2 = new(side2.Y, -side2.X);
                    values[at] = a + outerSum + innerSum;
                    values[at + width] = center1 + turn1;
                    values[at + 2 * width] = center2 + turn2;
                    values[at + 3 * width] = center2 - turn2;
                    values[at + 4 * width] = center1 - turn1;
                }
            }
        }

        private static Vector2 Rotate(Vector2 value, Vector2 factor) =>
            new(value.X * factor.X - value.Y * factor.Y, value.X * factor.Y + value.Y * factor.X);
    }
}
