// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264ConstantRegion
    {
        public static bool TryRead(ReadOnlySpan<byte> samples, int stride, int width, int height, out byte value)
        {
            value = samples[0];
            for (int row = 0; row < height; row++)
            {
                if (samples.Slice(row * stride, width).ContainsAnyExcept(value))
                {
                    return false;
                }
            }
            return true;
        }

        public static bool TryReadExtendedBlock(H264Picture picture, int plane, int x, int y, int size, int margin, out byte value)
        {
            int stride = picture.Stride(plane);
            ReadOnlySpan<byte> samples = picture.ReadPlane(plane);
            int left = Math.Max(0, x - margin);
            int top = Math.Max(0, y - margin);
            int right = Math.Min(stride, x + size + margin);
            int bottom = Math.Min(samples.Length / stride, y + size + margin);
            return TryRead(samples[(top * stride + left)..], stride, right - left, bottom - top, out value);
        }
    }
}
