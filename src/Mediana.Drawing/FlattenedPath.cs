// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Drawing
{
    internal class FlattenedPath
    {
        internal IReadOnlyList<PathContour> Contours { get; }
        internal int PointCount { get; }
        internal FlattenedPath(VectorPath path, Matrix3x2 transform, CancellationToken token)
        {
            Contours = PathFlattener.Flatten(path, transform, PathFlattener.AntialiasTolerance, token);
            foreach (PathContour contour in Contours) { PointCount += contour.Points.Count; }
        }
    }
}
