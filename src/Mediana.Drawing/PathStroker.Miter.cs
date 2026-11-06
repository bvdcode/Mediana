// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Drawing
{
    internal static partial class PathStroker
    {
        private static bool TryMiterContour(VectorPath output, PathContour contour, float radius, float miterLimit)
        {
            List<Vector2> points = contour.Points;
            int count = points.Count;
            if (count > 1 && points[0] == points[^1]) { count--; }
            if (count < 2) { return true; }
            List<Vector2> left = new(count), right = new(count);
            for (int i = 0; i < count; i++)
            {
                Vector2 at = points[i];
                Vector2 before = points[i == 0 ? contour.Closed ? count - 1 : 0 : i - 1];
                Vector2 after = points[i == count - 1 ? contour.Closed ? 0 : count - 1 : i + 1];
                Vector2 incoming = at - before, outgoing = after - at;
                if (!contour.Closed && i == 0) { incoming = outgoing; }
                if (!contour.Closed && i == count - 1) { outgoing = incoming; }
                if (incoming.LengthSquared() < 1e-8f || outgoing.LengthSquared() < 1e-8f) { return false; }
                incoming = Vector2.Normalize(incoming); outgoing = Vector2.Normalize(outgoing);
                float cosine = Vector2.Dot(incoming, outgoing), denominator = 1 + cosine;
                if (denominator <= 1e-5f) { return false; }
                Vector2 a = new(-incoming.Y, incoming.X), b = new(-outgoing.Y, outgoing.X);
                Vector2 miter = (a + b) * (radius / denominator);
                if (contour.Closed || (i > 0 && i < count - 1))
                {
                    float extent = Math.Min(Vector2.DistanceSquared(at, before), Vector2.DistanceSquared(at, after));
                    float longitudinal = Vector2.Dot(miter, incoming);
                    if (longitudinal * longitudinal > extent) { return false; }
                }
                if (miter.LengthSquared() <= radius * radius * miterLimit * miterLimit)
                {
                    left.Add(at + miter); right.Add(at - miter);
                }
                else if (PathFlattener.Cross(incoming, outgoing) > 0)
                {
                    left.Add(at + miter); right.Add(at - a * radius); right.Add(at - b * radius);
                }
                else
                {
                    left.Add(at + a * radius); left.Add(at + b * radius); right.Add(at - miter);
                }
            }
            if (contour.Closed)
            {
                AppendContour(output, right, false);
                AppendContour(output, left, true);
            }
            else
            {
                for (int i = left.Count - 1; i >= 0; i--) { right.Add(left[i]); }
                Polygon(output, right.ToArray());
            }
            return true;
        }

        private static void AppendContour(VectorPath output, List<Vector2> points, bool reverse)
        {
            if (reverse) { points.Reverse(); }
            output.MoveTo(points[0].X, points[0].Y);
            for (int i = 1; i < points.Count; i++) { output.LineTo(points[i].X, points[i].Y); }
            output.Close();
        }
    }
}
