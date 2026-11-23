// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;

namespace Mediana.Codecs.H264
{
    internal partial class H264Picture
    {
        public void WriteStationarySkip(H264Picture reference, int x, int y, int blocks = 1)
        {
            MarkStationarySkip(x, y, blocks, 0, -1);
            for (int plane = 0; plane < 3; plane++)
            {
                int horizontal = HorizontalScale(plane);
                int vertical = VerticalScale(plane);
                int stride = Stride(plane) * SampleBytes;
                int offset = y / vertical * stride + x / horizontal * SampleBytes;
                H264BlockRows.Copy(reference.ReadPlane(plane)[offset..], stride, Plane(plane)[offset..], stride,
                    16 / horizontal * SampleBytes * blocks, 16 / vertical);
            }
        }

        public void MarkStationarySkip(int x, int y, int blocks, int reference0, int reference1)
        {
            // Unvisited macroblocks have zero coefficients and vectors, with both reference indices unavailable.
            Span<byte> modes = Modes;
            Span<byte> past = reference0 >= 0 ? ReferenceIndices(H264ReferenceList.L0) : [];
            Span<byte> future = reference1 >= 0 ? ReferenceIndices(H264ReferenceList.L1) : [];
            int start = BlockIndex(0, x, y);
            int stride = Width / 4;
            int count = 4 * blocks;
            for (int row = 0; row < 4; row++, start += stride)
            {
                modes.Slice(start, count).Fill(InterMode);
                if (!past.IsEmpty) { past.Slice(start, count).Fill((byte)reference0); }
                if (!future.IsEmpty) { future.Slice(start, count).Fill((byte)reference1); }
            }
        }

        public void WriteInterPrediction(int x, int y, ReadOnlySpan<byte> prediction)
        {
            int offset = 0;
            for (int plane = 0; plane < 3; plane++)
            {
                int horizontal = HorizontalScale(plane);
                int vertical = VerticalScale(plane);
                int width = 16 / horizontal;
                int height = 16 / vertical;
                WritePredictionPlane(plane, x / horizontal, y / vertical, width, height, prediction[offset..], width);
                offset += width * height * SampleBytes;
            }
            MarkReconstructed(BlockIndex(0, x, y), Width / 4, 4);
        }

        public void WriteZeroResidualBlock(int plane, int x, int y, int size, ReadOnlySpan<byte> prediction, int predictionStride)
        {
            WritePredictionPlane(plane, x, y, size, size, prediction, predictionStride);
            if (plane == 0 || FullResolutionChroma)
            {
                MarkReconstructed(BlockIndex(plane, x, y), Stride(plane) / 4, size / 4);
            }
        }

        private void WritePredictionPlane(int plane, int x, int y, int width, int height,
            ReadOnlySpan<byte> prediction, int predictionStride)
        {
            (int offset, int stride) = SampleLocation(plane, x, y);
            H264BlockRows.Copy(prediction, predictionStride * SampleBytes, Plane(plane)[(offset * SampleBytes)..],
                stride * SampleBytes, width * SampleBytes, height);
            int countStride = Stride(plane) / 4;
            int first = BlockIndex(plane, x, y);
            Span<byte> counts = Counts(plane);
            for (int row = 0; row < height / 4; row++)
            {
                counts.Slice(first + row * countStride, width / 4).Clear();
            }
        }

        private void MarkReconstructed(int first, int stride, int size)
        {
            Span<byte> modes = Modes;
            for (int row = 0; row < size; row++)
            {
                Span<byte> cells = modes.Slice(first + row * stride, size);
                if (size == sizeof(uint))
                {
                    uint ready = MemoryMarshal.Read<uint>(cells) & 0x7f7f7f7fu;
                    MemoryMarshal.Write(cells, in ready);
                }
                else
                {
                    for (int column = 0; column < cells.Length; column++)
                    {
                        cells[column] &= 127;
                    }
                }
            }
        }
    }
}
