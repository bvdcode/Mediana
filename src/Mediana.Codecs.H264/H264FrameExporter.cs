// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Codecs.H264
{
    internal static class H264FrameExporter
    {
        public static VideoFrame Export(H264Picture picture, MediaBufferAllocator allocator,
            MediaTimestamp? timestamp, MediaDuration? duration, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            VideoFormat format = H264VideoFormat.Create(picture.Sequence);
            PlaneLayout[] layout = format.CreateTightLayout();
            using WritableMediaBuffer writable = allocator.Allocate(checked((int)layout[^1].EndOffset));
            for (int plane = 0; plane < layout.Length; plane++)
            {
                int scale = plane == 0 ? 1 : 2;
                int startX = picture.Sequence.CropLeft / scale;
                int startY = picture.Sequence.CropTop / scale;
                for (int row = 0; row < layout[plane].Height; row++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    picture.Plane(plane).Slice((startY + row) * picture.Stride(plane) + startX, layout[plane].RowBytes)
                        .CopyTo(writable.Memory.Span.Slice(layout[plane].Offset + row * layout[plane].Stride, layout[plane].RowBytes));
                }
            }
            using MediaBuffer buffer = writable.Publish();
            return new VideoFrame(format, buffer, layout, timestamp, duration);
        }
    }
}
