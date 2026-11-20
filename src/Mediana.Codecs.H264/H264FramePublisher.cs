// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Codecs.H264
{
    internal static class H264FramePublisher
    {
        public static VideoFrame Publish(H264Picture picture,
            MediaTimestamp? timestamp, MediaDuration? duration, CancellationToken cancellationToken, VideoColorDescription? colorDescription = null,
            VideoAspectRatioDescription? aspectRatioDescription = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            VideoFormat format = H264VideoFormat.Create(picture.Sequence, colorDescription, aspectRatioDescription);
            PlaneLayout[] layout = format.CreateTightLayout();
            int offset = 0;
            for (int plane = 0; plane < layout.Length; plane++)
            {
                int scale = plane == 0 ? 1 : 2;
                int startX = picture.Sequence.CropLeft / scale * picture.SampleBytes;
                int verticalScale = picture.VerticalScale(plane);
                int startY = picture.Sequence.CropTop / verticalScale;
                int stride = picture.Stride(plane) * picture.SampleBytes;
                layout[plane] = new(checked(offset + startY * stride + startX), stride, layout[plane].RowBytes, layout[plane].Height);
                offset += checked(stride * (picture.Height / verticalScale));
            }
            cancellationToken.ThrowIfCancellationRequested();
            using MediaBuffer buffer = picture.PublishPixels();
            return new VideoFrame(format, buffer, layout, timestamp, duration);
        }
    }
}
