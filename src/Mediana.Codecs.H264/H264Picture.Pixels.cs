// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Codecs.H264
{
    internal partial class H264Picture
    {
        private WritableMediaBuffer? writablePixels;
        private MediaBuffer? publishedPixels;
        private ReadOnlyMemory<byte> pixelMemory;

        public Span<byte> Plane(int index)
        {
            ObjectDisposedException.ThrowIf(pixelMemory.IsEmpty, this);
            if (writablePixels is null)
            {
                throw new InvalidOperationException("Published H.264 picture pixels cannot be modified.");
            }
            (int offset, int length) = PlaneRange(index);
            return writablePixels.Memory.Span.Slice(offset, length);
        }

        public ReadOnlySpan<byte> ReadPlane(int index)
        {
            ObjectDisposedException.ThrowIf(pixelMemory.IsEmpty, this);
            (int offset, int length) = PlaneRange(index);
            return pixelMemory.Span.Slice(offset, length);
        }

        public MediaBuffer PublishPixels()
        {
            ObjectDisposedException.ThrowIf(pixelMemory.IsEmpty, this);
            if (publishedPixels is null)
            {
                publishedPixels = writablePixels!.Publish();
                writablePixels = null;
            }
            return publishedPixels.Retain();
        }

        private (int Offset, int Length) PlaneRange(int index) => index switch
        {
            0 => (0, lumaBytes),
            1 => (lumaBytes, chromaBytes),
            2 => (lumaBytes + chromaBytes, chromaBytes),
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
    }
}
