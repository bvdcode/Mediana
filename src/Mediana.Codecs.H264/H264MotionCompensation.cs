// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264MotionCompensation
    {
        public static void Luma(H264Picture reference, int x, int y, int motionX, int motionY,
            int width, int height, Span<byte> output)
        {
            ValidateOutput(width, height, output);
            ReadOnlySpan<byte> samples = reference.Plane(0);
            long sourceX = (long)x + (motionX >> 2);
            long sourceY = (long)y + (motionY >> 2);
            int dx = motionX & 3;
            int dy = motionY & 3;
            if ((dx | dy) == 0)
            {
                H264BorderExtension.Copy(samples, reference.Width, reference.Height, sourceX, sourceY, width, height, output);
                return;
            }
            H264LumaInterpolation.Apply(samples, reference.Width, reference.Height, sourceX, sourceY, dx, dy, width, height, output);
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
            if (((motionX | motionY) & 7) == 0)
            {
                H264BorderExtension.Copy(samples, stride, rows, (long)x + (motionX >> 3), (long)y + (motionY >> 3), width, height, output);
                return;
            }
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

        private static int Sample(ReadOnlySpan<byte> samples, int stride, int height, long x, long y) =>
            samples[(int)Math.Clamp(y, 0, height - 1) * stride + (int)Math.Clamp(x, 0, stride - 1)];

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
