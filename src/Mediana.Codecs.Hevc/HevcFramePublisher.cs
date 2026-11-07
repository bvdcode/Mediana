// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Codecs.Hevc
{
    internal static class HevcFramePublisher
    {
        public static VideoFrame Publish(DecodedPicture picture, MediaTimestamp? timestamp, MediaDuration? duration,
            VideoColorDescription? color, VideoAspectRatioDescription? aspect)
        {
            SequenceParameters sequence = picture.Sequence;
            VideoFormat format = HevcVideoFormat.Create(sequence, color, aspect);
            PlaneLayout[] planes = format.CreateTightLayout();
            int bytes = (sequence.BitDepth + 7) / 8, offset = 0;
            for (int i = 0; i < planes.Length; i++)
            {
                int scaleX = i == 0 ? 1 : sequence.SubWidthC, scaleY = i == 0 ? 1 : sequence.SubHeightC;
                int stride = checked(sequence.Width / scaleX * bytes);
                int start = checked(offset + sequence.CropTop / scaleY * stride + sequence.CropLeft / scaleX * bytes);
                planes[i] = new(start, stride, planes[i].RowBytes, planes[i].Height);
                offset = checked(offset + stride * (sequence.Height / scaleY));
            }
            using MediaBuffer pixels = picture.PublishPixels();
            return new(format, pixels, planes, timestamp, duration);
        }
    }
}
