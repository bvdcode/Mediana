// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Codecs.H264
{
    internal static class H264FieldFramePublisher
    {
        public static VideoFrame Combine(H264QueuedField first, VideoFrame second, H264PictureOrder order,
            MediaBufferAllocator allocator, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            VideoFormat a = first.Frame.Format;
            VideoFormat b = second.Format;
            if (a.Width != b.Width || a.Height != b.Height || a.PixelFormat != b.PixelFormat || a.Color != b.Color ||
                a.SampleAspectRatio != b.SampleAspectRatio || !Equals(a.ColorDescription, b.ColorDescription))
            {
                throw new InvalidDataException("Complementary H.264 fields have different sample formats.");
            }
            VideoFrame top;
            VideoFrame bottom;
            switch (a.ScanMode)
            {
                case VideoScanMode.TopField when b.ScanMode == VideoScanMode.BottomField:
                    top = first.Frame;
                    bottom = second;
                    break;
                case VideoScanMode.BottomField when b.ScanMode == VideoScanMode.TopField:
                    top = second;
                    bottom = first.Frame;
                    break;
                default:
                    throw new InvalidDataException("Complementary H.264 fields require opposite parity.");
            }
            VideoFrame leading = first.Frame;
            if (order.Value < first.Order.Value)
            {
                leading = second;
            }
            (VideoScanMode scanMode, VideoFieldParity leadingParity) = leading.Format.ScanMode switch
            {
                VideoScanMode.TopField => (VideoScanMode.TopFieldFirst, VideoFieldParity.Top),
                VideoScanMode.BottomField => (VideoScanMode.BottomFieldFirst, VideoFieldParity.Bottom),
                _ => throw new InvalidOperationException("A field is required for presentation order.")
            };
            VideoFormat format = new(a.Width, checked(a.Height + b.Height), a.PixelFormat, a.Color,
                a.SampleAspectRatio, a.ColorDescription, scanMode);
            PlaneLayout[] layout = format.CreateTightLayout();
            using WritableMediaBuffer writable = allocator.Allocate(checked((int)layout[^1].EndOffset));
            for (int plane = 0; plane < layout.Length; plane++)
            {
                PlaneLayout destination = layout[plane];
                for (int row = 0; row < top.Planes[plane].Height; row++)
                {
                    token.ThrowIfCancellationRequested();
                    top.GetRow(plane, row).CopyTo(writable.Span.Slice(destination.Offset + row * 2 * destination.Stride, destination.RowBytes));
                    bottom.GetRow(plane, row).CopyTo(writable.Span.Slice(destination.Offset + (row * 2 + 1) * destination.Stride, destination.RowBytes));
                }
            }
            token.ThrowIfCancellationRequested();
            using MediaBuffer buffer = writable.Publish();
            VideoFramePresentation? presentation = H264Presentation.CombineFields(first.Frame.Presentation, second.Presentation, leadingParity);
            return new(format, buffer, layout, leading.Timestamp, CombineDuration(first.Frame.Duration, second.Duration), presentation);
        }

        private static MediaDuration? CombineDuration(MediaDuration? first, MediaDuration? second)
        {
            if (first is not MediaDuration a || second is not MediaDuration b)
            {
                return null;
            }
            TimeBase clock = TimeBase.Common(a.TimeBase, b.TimeBase);
            long aTicks = new MediaTimestamp(a.Value, a.TimeBase).Rescale(clock, TimestampRounding.TowardZero).Value;
            long bTicks = new MediaTimestamp(b.Value, b.TimeBase).Rescale(clock, TimestampRounding.TowardZero).Value;
            return new(checked(aTicks + bTicks), clock);
        }
    }
}
