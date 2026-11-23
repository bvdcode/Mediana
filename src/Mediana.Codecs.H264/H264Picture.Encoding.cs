// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal partial class H264Picture
    {
        public void WriteStationarySkip(H264Picture reference, int x, int y, int blocks = 1)
        {
            // Each encoder pass starts with cleared counts and vectors. This block has not been coded yet.
            int blockStride = Width / 4;
            int first = y / 4 * blockStride + x / 4;
            Span<byte> modes = Modes;
            Span<byte> indices = ReferenceIndices(H264ReferenceList.L0);
            for (int row = 0; row < 4; row++)
            {
                int start = first + row * blockStride;
                modes.Slice(start, 4 * blocks).Fill(InterMode);
                indices.Slice(start, 4 * blocks).Clear();
            }
            for (int plane = 0; plane < 3; plane++)
            {
                int scale = HorizontalScale(plane);
                int stride = Stride(plane);
                int left = x / scale;
                int top = y / VerticalScale(plane);
                int offset = top * stride + left;
                ReadOnlySpan<byte> previous = reference.ReadPlane(plane)[offset..];
                Span<byte> pixels = Plane(plane)[offset..];
                int size = 16 / scale;
                for (int row = 0; row < size; row++)
                {
                    previous.Slice(row * stride, size * blocks).CopyTo(pixels[(row * stride)..]);
                }
            }
        }

        public void ResetForEncoding()
        {
            ObjectDisposedException.ThrowIf(pixelMemory.IsEmpty, this);
            if (writablePixels is null || referenceLists != 1)
            {
                throw new InvalidOperationException("Only unpublished I/P reconstruction storage can be reused for encoding.");
            }
            storage.Span.Clear();
            Modes.Fill(byte.MaxValue);
            ReferenceIndices(H264ReferenceList.L0).Fill(byte.MaxValue);
            FirstMacroblock = 0;
        }
    }
}
