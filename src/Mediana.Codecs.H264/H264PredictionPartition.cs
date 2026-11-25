// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal readonly ref struct H264PredictionPartition
    {
        private readonly Span<byte> destination;
        private readonly int stride;
        private readonly int rowBytes;
        private readonly int rows;

        public H264PredictionPartition(H264Picture picture, int plane, int x, int y, int width, int height, Span<byte> prediction)
        {
            int planeOffset = plane switch
            {
                0 => 0,
                1 => picture.LumaMacroblockBytes,
                2 => picture.LumaMacroblockBytes + picture.ChromaMacroblockBytes,
                _ => throw new ArgumentOutOfRangeException(nameof(plane))
            };
            int horizontalScale = picture.HorizontalScale(plane);
            int verticalScale = picture.VerticalScale(plane);
            stride = 16 / horizontalScale * picture.SampleBytes;
            rowBytes = width / horizontalScale * picture.SampleBytes;
            rows = height / verticalScale;
            int start = planeOffset + y % 16 / verticalScale * stride + x % 16 / horizontalScale * picture.SampleBytes;
            destination = prediction.Slice(start, (rows - 1) * stride + rowBytes);
        }

        public Span<byte> PackedSamples(Span<byte> scratch) => rowBytes == stride ? destination : scratch[..(rowBytes * rows)];

        public void Complete(ReadOnlySpan<byte> samples)
        {
            if (rowBytes != stride)
            {
                H264BlockRows.Copy(samples, rowBytes, destination, stride, rowBytes, rows);
            }
        }
    }
}
