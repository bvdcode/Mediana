namespace Mediana.Processing
{
    internal readonly record struct RgbMatrix(double M11, double M12, double M13, double M21, double M22,
        double M23, double M31, double M32, double M33)
    {
        public static RgbMatrix Identity => new(1, 0, 0, 0, 1, 0, 0, 0, 1);

        public (double Red, double Green, double Blue) Transform(double red, double green, double blue) =>
            (M11 * red + M12 * green + M13 * blue,
             M21 * red + M22 * green + M23 * blue,
             M31 * red + M32 * green + M33 * blue);

        public RgbMatrix Multiply(RgbMatrix other) => new(
            M11 * other.M11 + M12 * other.M21 + M13 * other.M31,
            M11 * other.M12 + M12 * other.M22 + M13 * other.M32,
            M11 * other.M13 + M12 * other.M23 + M13 * other.M33,
            M21 * other.M11 + M22 * other.M21 + M23 * other.M31,
            M21 * other.M12 + M22 * other.M22 + M23 * other.M32,
            M21 * other.M13 + M22 * other.M23 + M23 * other.M33,
            M31 * other.M11 + M32 * other.M21 + M33 * other.M31,
            M31 * other.M12 + M32 * other.M22 + M33 * other.M32,
            M31 * other.M13 + M32 * other.M23 + M33 * other.M33);

        public RgbMatrix ScaleColumns(double red, double green, double blue) => new(
            M11 * red, M12 * green, M13 * blue,
            M21 * red, M22 * green, M23 * blue,
            M31 * red, M32 * green, M33 * blue);

        public RgbMatrix Inverse()
        {
            double determinant = M11 * (M22 * M33 - M23 * M32) - M12 * (M21 * M33 - M23 * M31)
                + M13 * (M21 * M32 - M22 * M31);
            if (determinant == 0 || !double.IsFinite(determinant))
            {
                throw new InvalidOperationException("The RGB matrix is not invertible.");
            }
            return new RgbMatrix(
                (M22 * M33 - M23 * M32) / determinant, (M13 * M32 - M12 * M33) / determinant, (M12 * M23 - M13 * M22) / determinant,
                (M23 * M31 - M21 * M33) / determinant, (M11 * M33 - M13 * M31) / determinant, (M13 * M21 - M11 * M23) / determinant,
                (M21 * M32 - M22 * M31) / determinant, (M12 * M31 - M11 * M32) / determinant, (M11 * M22 - M12 * M21) / determinant);
        }
    }
}
