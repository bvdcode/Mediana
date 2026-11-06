// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Drawing
{
    internal static partial class PathFlattener
    {
        private static void FlattenQuadraticCoverage(List<Vector2> points, Vector2 a, Vector2 b, Vector2 c,
            float tolerance, int depth, ref int remaining, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            // The quadratic control weight is at most 1/2 over the entire curve.
            if (WithinChord(a, c, b, 4 * tolerance * tolerance) || depth >= 12)
            {
                Consume(ref remaining); points.Add(c); return;
            }
            Vector2 ab = (a + b) / 2, bc = (b + c) / 2, mid = (ab + bc) / 2;
            FlattenQuadraticCoverage(points, a, ab, mid, tolerance, depth + 1, ref remaining, token);
            FlattenQuadraticCoverage(points, mid, bc, c, tolerance, depth + 1, ref remaining, token);
        }

        private static void FlattenCubicCoverage(List<Vector2> points, Vector2 a, Vector2 b, Vector2 c, Vector2 d,
            float tolerance, int depth, ref int remaining, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            // The two cubic control weights sum to at most 3/4.
            float squaredTolerance = 16f / 9 * tolerance * tolerance;
            if ((WithinChord(a, d, b, squaredTolerance) && WithinChord(a, d, c, squaredTolerance)) || depth >= 12)
            {
                Consume(ref remaining); points.Add(d); return;
            }
            Vector2 ab = (a + b) / 2, bc = (b + c) / 2, cd = (c + d) / 2;
            Vector2 abc = (ab + bc) / 2, bcd = (bc + cd) / 2, mid = (abc + bcd) / 2;
            FlattenCubicCoverage(points, a, ab, abc, mid, tolerance, depth + 1, ref remaining, token);
            FlattenCubicCoverage(points, mid, bcd, cd, d, tolerance, depth + 1, ref remaining, token);
        }

        private static bool WithinChord(Vector2 start, Vector2 end, Vector2 control, float squaredTolerance)
        {
            Vector2 chord = end - start, offset = control - start;
            float squaredLength = chord.LengthSquared(), projection = Vector2.Dot(chord, offset);
            if (projection <= 0) { return offset.LengthSquared() <= squaredTolerance; }
            if (projection >= squaredLength) { return Vector2.DistanceSquared(control, end) <= squaredTolerance; }
            float perpendicular = Cross(chord, offset);
            return perpendicular * perpendicular <= squaredTolerance * squaredLength;
        }
    }
}
