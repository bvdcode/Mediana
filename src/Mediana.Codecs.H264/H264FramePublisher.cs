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
            VideoAspectRatioDescription? aspectRatioDescription = null, VideoFramePresentation? presentation = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            VideoScanMode scanMode = picture.Structure switch
            {
                H264PictureStructure.Frame when picture.Layout.MacroblockAdaptiveFrameField &&
                    presentation is { Unit: VideoPresentationUnit.Field, SourceScan: not VideoSourceScan.Progressive } => presentation.Value.FirstField switch
                    {
                        VideoFieldParity.Top => VideoScanMode.TopFieldFirst,
                        VideoFieldParity.Bottom => VideoScanMode.BottomFieldFirst,
                        _ => throw new InvalidDataException("An adaptive field presentation has no field order.")
                    },
                H264PictureStructure.Frame => VideoScanMode.Progressive,
                H264PictureStructure.TopField => VideoScanMode.TopField,
                H264PictureStructure.BottomField => VideoScanMode.BottomField,
                _ => throw new ArgumentOutOfRangeException(nameof(picture))
            };
            VideoFormat format = H264VideoFormat.Create(picture.Sequence, colorDescription, aspectRatioDescription, scanMode);
            PlaneLayout[] layout = format.CreateTightLayout();
            int offset = 0;
            for (int plane = 0; plane < layout.Length; plane++)
            {
                int scale = plane == 0 ? 1 : 2;
                int startX = picture.Sequence.CropLeft / scale * picture.SampleBytes;
                int verticalScale = picture.VerticalScale(plane);
                int startY = picture.Sequence.CropTop / verticalScale / (picture.Layout.IsField ? 2 : 1);
                int stride = picture.Stride(plane) * picture.SampleBytes;
                layout[plane] = new(checked(offset + startY * stride + startX), stride, layout[plane].RowBytes, layout[plane].Height);
                offset += checked(stride * (picture.Height / verticalScale));
            }
            cancellationToken.ThrowIfCancellationRequested();
            using MediaBuffer buffer = picture.PublishPixels();
            return new VideoFrame(format, buffer, layout, timestamp, duration, presentation);
        }
    }
}
