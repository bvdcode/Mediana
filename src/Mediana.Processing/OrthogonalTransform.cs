// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Processing
{
    internal readonly record struct OrthogonalTransform(int A, int B, int C, int D)
    {
        public static OrthogonalTransform Identity { get; } = new(1, 0, 0, 1);
        public bool SwapsAxes => A == 0;

        public static OrthogonalTransform FromMatrix(VideoDisplayMatrix matrix)
        {
            matrix.Validate();
            if (matrix.M13 != 0 || matrix.M23 != 0 || matrix.M33 != 1 ||
                !IsUnit(matrix.M11) || !IsUnit(matrix.M12) || !IsUnit(matrix.M21) || !IsUnit(matrix.M22))
            {
                throw new NotSupportedException("Display fitting supports exact orthogonal rotation and reflection matrices only.");
            }
            int a = (int)matrix.M11;
            int b = (int)matrix.M12;
            int c = (int)matrix.M21;
            int d = (int)matrix.M22;
            if (a * a + b * b != 1 || c * c + d * d != 1 || a * c + b * d != 0)
            {
                throw new NotSupportedException("Display fitting requires perpendicular unit axes.");
            }
            return new OrthogonalTransform(a, b, c, d);
        }

        public int SourceX(int x, int y, int width) => A * x + B * y + (A + B < 0 ? width - 1 : 0);
        public int SourceY(int x, int y, int height) => C * x + D * y + (C + D < 0 ? height - 1 : 0);
        private static bool IsUnit(double value) => value is -1 or 0 or 1;
    }
}
