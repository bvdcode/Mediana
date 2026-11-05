// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using Mediana.Drawing;
namespace Mediana.Documents.Pdf
{
    internal record ShadingCommand(bool Radial, double[] Coordinates, bool ExtendStart, bool ExtendEnd,
        Rgba32[] Colors, Matrix3x2 Transform, VectorPath? Bounds, ColorBlendMode Blend) : IPdfDrawCommand
    {
        public void Draw(RasterCanvas canvas, Matrix3x2 device, CancellationToken cancellationToken)
        {
            canvas.Save();
            try
            {
                if (Bounds is not null) { canvas.ClipPath(Bounds, Transform * device, cancellationToken: cancellationToken); }
                canvas.DrawShader(point => Colors[(int)Math.Round(Math.Clamp(Parameter(point), 0, 1) * (Colors.Length - 1))],
                    point => !double.IsNaN(Parameter(point)), Transform * device, Blend, cancellationToken);
            }
            finally { canvas.Restore(); }
        }
        private double Parameter(Vector2 point)
        {
            double px = point.X - Coordinates[0], py = point.Y - Coordinates[1];
            if (!Radial)
            {
                double dx = Coordinates[2] - Coordinates[0], dy = Coordinates[3] - Coordinates[1];
                double denominator = (dx * dx) + (dy * dy);
                if (denominator == 0) { return double.NaN; }
                double t = ((px * dx) + (py * dy)) / denominator;
                return InRange(t) ? t : double.NaN;
            }
            double vx = Coordinates[3] - Coordinates[0], vy = Coordinates[4] - Coordinates[1];
            double radius = Coordinates[2], delta = Coordinates[5] - radius;
            double a = (vx * vx) + (vy * vy) - (delta * delta);
            double b = -2 * ((px * vx) + (py * vy) + (radius * delta));
            double c = (px * px) + (py * py) - (radius * radius);
            if (a == 0)
            {
                if (b == 0) { return double.NaN; }
                double t = -c / b;
                return Valid(t) ? t : double.NaN;
            }
            double discriminant = (b * b) - (4 * a * c);
            if (discriminant < 0) { return double.NaN; }
            double root = Math.Sqrt(discriminant);
            double first = (-b - root) / (2 * a), second = (-b + root) / (2 * a);
            double greater = Math.Max(first, second), lesser = Math.Min(first, second);
            if (Valid(greater)) { return greater; }
            return Valid(lesser) ? lesser : double.NaN;
            bool Valid(double t) => InRange(t) && radius + (t * delta) >= 0;
        }
        private bool InRange(double value) => double.IsFinite(value) && (value >= 0 || ExtendStart) && (value <= 1 || ExtendEnd);
    }
}
