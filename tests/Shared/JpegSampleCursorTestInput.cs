// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Jpeg;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    internal static class JpegSampleCursorTestInput
    {
        public static void Verify()
        {
            (int A, int B, int C, int D)[] transforms = [(1, 0, 0, 1), (-1, 0, 0, 1), (-1, 0, 0, -1), (1, 0, 0, -1),
                (0, 1, 1, 0), (0, 1, -1, 0), (0, -1, -1, 0), (0, -1, 1, 0)];
            foreach (int width in new[] { 1, 5, 13 })
            foreach (int height in new[] { 1, 7, 9 })
            for (int maximumX = 1; maximumX <= 4; maximumX++)
            for (int maximumY = 1; maximumY <= 4; maximumY++)
            for (int horizontal = 1; horizontal <= maximumX; horizontal++)
            for (int vertical = 1; vertical <= maximumY; vertical++)
            {
                int columns = (width * horizontal + maximumX - 1) / maximumX;
                int rows = (height * vertical + maximumY - 1) / maximumY;
                int stride = columns + 3;
                byte[] samples = new byte[stride * rows];
                for (int index = 0; index < samples.Length; index++) { samples[index] = (byte)((index * 97 + 37) % 256); }
                JpegComponent component = new(1, horizontal, vertical, 0, 1)
                { SampleWidth = columns, SampleHeight = rows, SampleStride = stride, Samples = samples };
                JpegSampleLayout layout = new(width, height, maximumX, maximumY, [component]);
                foreach ((int a, int b, int c, int d) in transforms)
                {
                    OrthogonalTransform transform = OrthogonalTransform.FromMatrix(new(a, b, 0, c, d, 0, 0, 0, 1));
                    int outputWidth = transform.SwapsAxes ? height : width;
                    int outputHeight = transform.SwapsAxes ? width : height;
                    foreach (bool centered in new[] { false, true })
                    {
                        for (int y = 0; y < outputHeight; y++)
                        {
                            JpegSampleCursor cursor = new(layout, component, transform.SourceX(0, y, width), transform.SourceY(0, y, height),
                                transform, centered ? maximumX : 0, centered ? maximumY : 0);
                            for (int x = 0; x < outputWidth; x++)
                            {
                                int sx = transform.SourceX(x, y, width), sy = transform.SourceY(x, y, height);
                                int expected = Reference(samples, stride, columns, rows, sx, sy, horizontal, vertical, maximumX, maximumY, centered);
                                int actual = cursor.ReadNext();
                                if (expected != actual)
                                {
                                    throw new InvalidDataException($"JPEG sample cursor changed {width}x{height}, factors {horizontal}/{maximumX} {vertical}/{maximumY}, at {sx},{sy}: {expected} != {actual}.");
                                }
                            }
                        }
                    }
                }
            }
        }

        private static int Reference(byte[] samples, int stride, int columns, int rows, int x, int y,
            int horizontal, int vertical, int maximumX, int maximumY, bool centered)
        {
            if (horizontal == maximumX && vertical == maximumY) { return samples[y * stride + x]; }
            int dx = 2 * maximumX, dy = 2 * maximumY;
            int nx = 2 * x * horizontal + (centered ? horizontal - maximumX : 0);
            int ny = 2 * y * vertical + (centered ? vertical - maximumY : 0);
            int left = (int)Math.Floor((double)nx / dx), top = (int)Math.Floor((double)ny / dy);
            int fx = nx - left * dx, fy = ny - top * dy;
            int sum = 0;
            for (int j = 0; j < 2; j++)
            for (int i = 0; i < 2; i++)
            {
                int xx = Math.Clamp(left + i, 0, columns - 1), yy = Math.Clamp(top + j, 0, rows - 1);
                int wx = i == 0 ? dx - fx : fx, wy = j == 0 ? dy - fy : fy;
                sum += samples[yy * stride + xx] * wx * wy;
            }
            return (sum + dx * dy / 2) / (dx * dy);
        }
    }
}
