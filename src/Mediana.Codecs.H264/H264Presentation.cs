// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Codecs.H264
{
    internal static class H264Presentation
    {
        public static VideoFramePresentation Create(H264SliceHeader header, H264PictureOrder order, H264PictureTiming? timing,
            bool ignoreInconsistentTiming, out bool ignoredTiming)
        {
            H264DisplayStructure structure = timing?.DisplayStructure ?? InferStructure(header, order);
            ignoredTiming = !IsConsistent(structure, header, order);
            if (ignoredTiming)
            {
                if (!ignoreInconsistentTiming)
                {
                    throw new InvalidDataException("The H.264 picture-timing display structure contradicts its coded picture or field order.");
                }
                structure = InferStructure(header, order);
                timing = null;
            }
            VideoSourceScan source = ReadSourceScan(timing?.SourceScanTypes ?? H264SourceScanTypes.None);
            return structure switch
            {
                H264DisplayStructure.Frame => VideoFramePresentation.Frames(1, source),
                H264DisplayStructure.TopField => VideoFramePresentation.Fields(VideoFieldParity.Top, 1, source),
                H264DisplayStructure.BottomField => VideoFramePresentation.Fields(VideoFieldParity.Bottom, 1, source),
                H264DisplayStructure.TopBottom => VideoFramePresentation.Fields(VideoFieldParity.Top, 2, source),
                H264DisplayStructure.BottomTop => VideoFramePresentation.Fields(VideoFieldParity.Bottom, 2, source),
                H264DisplayStructure.TopBottomTop => VideoFramePresentation.Fields(VideoFieldParity.Top, 3, source),
                H264DisplayStructure.BottomTopBottom => VideoFramePresentation.Fields(VideoFieldParity.Bottom, 3, source),
                H264DisplayStructure.FrameDouble => VideoFramePresentation.Frames(2, source),
                H264DisplayStructure.FrameTriple => VideoFramePresentation.Frames(3, source),
                _ => throw new ArgumentOutOfRangeException(nameof(timing))
            };
        }

        public static VideoFramePresentation? CombineFields(VideoFramePresentation? first, VideoFramePresentation? second,
            VideoFieldParity leading)
        {
            if (first is not VideoFramePresentation a || second is not VideoFramePresentation b)
            {
                return null;
            }
            VideoSourceScan source = (a.SourceScan, b.SourceScan) switch
            {
                (VideoSourceScan.Progressive, VideoSourceScan.Progressive) => VideoSourceScan.Progressive,
                (VideoSourceScan.Interlaced, VideoSourceScan.Interlaced) => VideoSourceScan.Interlaced,
                (VideoSourceScan.Progressive, VideoSourceScan.Interlaced) or
                (VideoSourceScan.Interlaced, VideoSourceScan.Progressive) => VideoSourceScan.Mixed,
                (VideoSourceScan.Mixed, VideoSourceScan.Unknown or VideoSourceScan.Progressive or VideoSourceScan.Interlaced or VideoSourceScan.Mixed) or
                (VideoSourceScan.Unknown or VideoSourceScan.Progressive or VideoSourceScan.Interlaced, VideoSourceScan.Mixed) => VideoSourceScan.Mixed,
                (VideoSourceScan.Unknown, VideoSourceScan.Unknown or VideoSourceScan.Progressive or VideoSourceScan.Interlaced) or
                (VideoSourceScan.Progressive or VideoSourceScan.Interlaced, VideoSourceScan.Unknown) => VideoSourceScan.Unknown,
                _ => throw new ArgumentException("The field presentation has an invalid source scan type.")
            };
            return VideoFramePresentation.Fields(leading, 2, source);
        }

        private static H264DisplayStructure InferStructure(H264SliceHeader header, H264PictureOrder order) => header.Structure switch
        {
            H264PictureStructure.TopField => H264DisplayStructure.TopField,
            H264PictureStructure.BottomField => H264DisplayStructure.BottomField,
            H264PictureStructure.Frame when order.Top == order.Bottom => H264DisplayStructure.Frame,
            H264PictureStructure.Frame when order.Top < order.Bottom => H264DisplayStructure.TopBottom,
            H264PictureStructure.Frame => H264DisplayStructure.BottomTop,
            _ => throw new ArgumentOutOfRangeException(nameof(header))
        };

        private static bool IsConsistent(H264DisplayStructure structure, H264SliceHeader header, H264PictureOrder order)
        {
            bool frame = header.Structure == H264PictureStructure.Frame;
            return structure switch
            {
                H264DisplayStructure.Frame => frame && order.Top == order.Bottom,
                H264DisplayStructure.TopField => header.Structure == H264PictureStructure.TopField,
                H264DisplayStructure.BottomField => header.Structure == H264PictureStructure.BottomField,
                H264DisplayStructure.TopBottom or H264DisplayStructure.TopBottomTop => frame && order.Top <= order.Bottom,
                H264DisplayStructure.BottomTop or H264DisplayStructure.BottomTopBottom => frame && order.Bottom <= order.Top,
                H264DisplayStructure.FrameDouble or H264DisplayStructure.FrameTriple =>
                    frame && order.Top == order.Bottom && header.Sequence.Vui?.FixedFrameRate == true,
                _ => throw new ArgumentOutOfRangeException(nameof(structure))
            };
        }

        private static VideoSourceScan ReadSourceScan(H264SourceScanTypes scans) => scans switch
        {
            H264SourceScanTypes.Progressive => VideoSourceScan.Progressive,
            H264SourceScanTypes.Interlaced => VideoSourceScan.Interlaced,
            H264SourceScanTypes.None or H264SourceScanTypes.Unknown or
            (H264SourceScanTypes.Progressive | H264SourceScanTypes.Unknown) or
            (H264SourceScanTypes.Interlaced | H264SourceScanTypes.Unknown) => VideoSourceScan.Unknown,
            (H264SourceScanTypes.Progressive | H264SourceScanTypes.Interlaced) or
            (H264SourceScanTypes.Progressive | H264SourceScanTypes.Interlaced | H264SourceScanTypes.Unknown) => VideoSourceScan.Mixed,
            _ => throw new ArgumentOutOfRangeException(nameof(scans))
        };
    }
}
