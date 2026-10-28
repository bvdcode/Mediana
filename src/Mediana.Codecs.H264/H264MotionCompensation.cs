namespace Mediana.Codecs.H264
{
    internal static class H264MotionCompensation
    {
        private static ReadOnlySpan<int> Filter => [1, -5, 20, 20, -5, 1];

        public static void Luma(H264Picture reference, int x, int y, int motionX, int motionY,
            int width, int height, Span<byte> output)
        {
            ValidateOutput(width, height, output);
            ReadOnlySpan<byte> samples = reference.Plane(0);
            for (int row = 0; row < height; row++)
            {
                for (int column = 0; column < width; column++)
                {
                    long quarterX = 4L * ((long)x + column) + motionX;
                    long quarterY = 4L * ((long)y + row) + motionY;
                    output[row * width + column] = (byte)LumaSample(samples, reference.Width, reference.Height, quarterX, quarterY);
                }
            }
        }

        public static void Chroma(H264Picture reference, int plane, int x, int y, int motionX, int motionY,
            int width, int height, Span<byte> output)
        {
            if (plane is not (1 or 2))
            {
                throw new ArgumentOutOfRangeException(nameof(plane));
            }
            ValidateOutput(width, height, output);
            ReadOnlySpan<byte> samples = reference.Plane(plane);
            int stride = reference.Width / 2;
            int rows = reference.Height / 2;
            for (int row = 0; row < height; row++)
            {
                for (int column = 0; column < width; column++)
                {
                    long eighthX = 8L * ((long)x + column) + motionX;
                    long eighthY = 8L * ((long)y + row) + motionY;
                    long ix = eighthX >> 3;
                    long iy = eighthY >> 3;
                    int dx = (int)(eighthX & 7);
                    int dy = (int)(eighthY & 7);
                    int a = Sample(samples, stride, rows, ix, iy);
                    int b = Sample(samples, stride, rows, ix + 1, iy);
                    int c = Sample(samples, stride, rows, ix, iy + 1);
                    int d = Sample(samples, stride, rows, ix + 1, iy + 1);
                    output[row * width + column] = (byte)(((8 - dx) * (8 - dy) * a + dx * (8 - dy) * b +
                        (8 - dx) * dy * c + dx * dy * d + 32) >> 6);
                }
            }
        }

        private static int LumaSample(ReadOnlySpan<byte> samples, int stride, int height, long quarterX, long quarterY)
        {
            long x = quarterX >> 2;
            long y = quarterY >> 2;
            // Rec. ITU-T H.264, Table 8-12. Half samples are clipped before quarter-sample averaging.
            return ((int)(quarterX & 3), (int)(quarterY & 3)) switch
            {
                (0, 0) => Sample(samples, stride, height, x, y),
                (1, 0) => Average(Sample(samples, stride, height, x, y), Horizontal(samples, stride, height, x, y)),
                (2, 0) => Horizontal(samples, stride, height, x, y),
                (3, 0) => Average(Horizontal(samples, stride, height, x, y), Sample(samples, stride, height, x + 1, y)),
                (0, 1) => Average(Sample(samples, stride, height, x, y), Vertical(samples, stride, height, x, y)),
                (1, 1) => Average(Horizontal(samples, stride, height, x, y), Vertical(samples, stride, height, x, y)),
                (2, 1) => Average(Horizontal(samples, stride, height, x, y), Diagonal(samples, stride, height, x, y)),
                (3, 1) => Average(Horizontal(samples, stride, height, x, y), Vertical(samples, stride, height, x + 1, y)),
                (0, 2) => Vertical(samples, stride, height, x, y),
                (1, 2) => Average(Vertical(samples, stride, height, x, y), Diagonal(samples, stride, height, x, y)),
                (2, 2) => Diagonal(samples, stride, height, x, y),
                (3, 2) => Average(Diagonal(samples, stride, height, x, y), Vertical(samples, stride, height, x + 1, y)),
                (0, 3) => Average(Vertical(samples, stride, height, x, y), Sample(samples, stride, height, x, y + 1)),
                (1, 3) => Average(Vertical(samples, stride, height, x, y), Horizontal(samples, stride, height, x, y + 1)),
                (2, 3) => Average(Diagonal(samples, stride, height, x, y), Horizontal(samples, stride, height, x, y + 1)),
                (3, 3) => Average(Vertical(samples, stride, height, x + 1, y), Horizontal(samples, stride, height, x, y + 1)),
                _ => throw new InvalidOperationException("An H.264 luma fraction is outside its two-bit range.")
            };
        }

        private static int Horizontal(ReadOnlySpan<byte> samples, int stride, int height, long x, long y) =>
            Math.Clamp((HorizontalSum(samples, stride, height, x, y) + 16) >> 5, 0, 255);

        private static int HorizontalSum(ReadOnlySpan<byte> samples, int stride, int height, long x, long y)
        {
            int sum = 0;
            for (int tap = 0; tap < 6; tap++)
            {
                sum += Filter[tap] * Sample(samples, stride, height, x + tap - 2, y);
            }
            return sum;
        }

        private static int Vertical(ReadOnlySpan<byte> samples, int stride, int height, long x, long y)
        {
            int sum = 0;
            for (int tap = 0; tap < 6; tap++)
            {
                sum += Filter[tap] * Sample(samples, stride, height, x, y + tap - 2);
            }
            return Math.Clamp((sum + 16) >> 5, 0, 255);
        }

        private static int Diagonal(ReadOnlySpan<byte> samples, int stride, int height, long x, long y)
        {
            int sum = 0;
            for (int tap = 0; tap < 6; tap++)
            {
                sum += Filter[tap] * HorizontalSum(samples, stride, height, x, y + tap - 2);
            }
            return Math.Clamp((sum + 512) >> 10, 0, 255);
        }

        private static int Sample(ReadOnlySpan<byte> samples, int stride, int height, long x, long y) =>
            samples[(int)Math.Clamp(y, 0, height - 1) * stride + (int)Math.Clamp(x, 0, stride - 1)];

        private static int Average(int a, int b) => (a + b + 1) >> 1;

        private static void ValidateOutput(int width, int height, Span<byte> output)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            if ((long)width * height != output.Length)
            {
                throw new ArgumentException("The H.264 prediction output must match its block dimensions.", nameof(output));
            }
        }
    }
}
