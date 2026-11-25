// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Codecs.H264
{
    internal partial class H264Picture
    {
        private WritableMediaBuffer? writablePixels;
        private MediaBuffer? publishedPixels;
        private Memory<byte> lumaPixels;
        private Memory<byte> firstChromaPixels;
        private Memory<byte> secondChromaPixels;

        public Span<byte> Plane(int index)
        {
            ObjectDisposedException.ThrowIf(lumaPixels.IsEmpty, this);
            if (writablePixels is null)
            {
                throw new InvalidOperationException("Published H.264 picture pixels cannot be modified.");
            }
            return GetPlane(index).Span;
        }

        public ReadOnlySpan<byte> ReadPlane(int index)
        {
            ObjectDisposedException.ThrowIf(lumaPixels.IsEmpty, this);
            return GetPlane(index).Span;
        }

        public MediaBuffer PublishPixels()
        {
            ObjectDisposedException.ThrowIf(lumaPixels.IsEmpty, this);
            if (publishedPixels is null)
            {
                publishedPixels = writablePixels!.Publish();
                writablePixels = null;
            }
            return publishedPixels.Retain();
        }

        private Memory<byte> GetPlane(int index) => index switch
        {
            0 => lumaPixels,
            1 => firstChromaPixels,
            2 => secondChromaPixels,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
    }
}
