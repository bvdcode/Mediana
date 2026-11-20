// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal partial class H264Picture
    {
        public void WriteZeroResidualBlock(int plane, int x, int y, int size, ReadOnlySpan<byte> prediction, int predictionStride)
        {
            (int offset, int stride) = SampleLocation(plane, x, y);
            Span<byte> pixels = Plane(plane);
            for (int row = 0; row < size; row++)
            {
                prediction.Slice(row * predictionStride * SampleBytes, size * SampleBytes)
                    .CopyTo(pixels.Slice((offset + row * stride) * SampleBytes, size * SampleBytes));
            }
            int blockSize = size / 4;
            int countStride = Stride(plane) / 4;
            int first = y / 4 * countStride + x / 4;
            Span<byte> counts = Counts(plane);
            for (int row = 0; row < blockSize; row++)
            {
                counts.Slice(first + row * countStride, blockSize).Clear();
            }
            if (plane == 0)
            {
                Span<byte> modes = Modes;
                for (int row = 0; row < blockSize; row++)
                {
                    for (int column = 0; column < blockSize; column++)
                    {
                        modes[first + row * countStride + column] &= 127;
                    }
                }
            }
        }
    }
}
