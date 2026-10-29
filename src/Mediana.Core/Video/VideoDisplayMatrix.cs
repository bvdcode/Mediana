// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Core.Video
{
    /// <summary>Row-vector display transform: [x y 1] times this matrix, followed by homogeneous division.
    /// Coordinates refer to the displayed image before placement on a presentation canvas.</summary>
    public readonly record struct VideoDisplayMatrix(double M11, double M12, double M13,
        double M21, double M22, double M23, double M31, double M32, double M33)
    {
        public static VideoDisplayMatrix Identity { get; } = new(1, 0, 0, 0, 1, 0, 0, 0, 1);

        /// <summary>Composes this transform followed by next. Does not apply either transform to pixels.</summary>
        public VideoDisplayMatrix Then(VideoDisplayMatrix next)
        {
            Validate();
            next.Validate();
            VideoDisplayMatrix result = new(
                M11 * next.M11 + M12 * next.M21 + M13 * next.M31,
                M11 * next.M12 + M12 * next.M22 + M13 * next.M32,
                M11 * next.M13 + M12 * next.M23 + M13 * next.M33,
                M21 * next.M11 + M22 * next.M21 + M23 * next.M31,
                M21 * next.M12 + M22 * next.M22 + M23 * next.M32,
                M21 * next.M13 + M22 * next.M23 + M23 * next.M33,
                M31 * next.M11 + M32 * next.M21 + M33 * next.M31,
                M31 * next.M12 + M32 * next.M22 + M33 * next.M32,
                M31 * next.M13 + M32 * next.M23 + M33 * next.M33);
            result.Validate();
            return result;
        }

        public void Validate()
        {
            ReadOnlySpan<double> coefficients = [M11, M12, M13, M21, M22, M23, M31, M32, M33];
            foreach (double coefficient in coefficients)
            {
                if (!double.IsFinite(coefficient))
                {
                    throw new ArgumentException("A display matrix must contain only finite coefficients.");
                }
            }
        }
    }
}
