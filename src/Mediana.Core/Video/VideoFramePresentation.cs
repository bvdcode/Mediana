// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Core.Video
{
    /// <summary>A display hint for one decoded surface. Does not replace its timestamp or duration.</summary>
    public readonly record struct VideoFramePresentation
    {
        private VideoFramePresentation(VideoPresentationUnit unit, int displayCount, VideoFieldParity? firstField,
            VideoSourceScan sourceScan)
        {
            Unit = unit;
            DisplayCount = displayCount;
            FirstField = firstField;
            SourceScan = sourceScan;
            Validate();
        }

        public VideoPresentationUnit Unit { get; }
        /// <summary>Number of consecutive frame displays or alternating field displays.</summary>
        public int DisplayCount { get; }
        public VideoFieldParity? FirstField { get; }
        public VideoSourceScan SourceScan { get; }

        public static VideoFramePresentation Frames(int displayCount = 1, VideoSourceScan sourceScan = VideoSourceScan.Unknown) =>
            new(VideoPresentationUnit.Frame, displayCount, null, sourceScan);

        public static VideoFramePresentation Fields(VideoFieldParity firstField, int displayCount,
            VideoSourceScan sourceScan = VideoSourceScan.Unknown) => new(VideoPresentationUnit.Field, displayCount, firstField, sourceScan);

        public void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(DisplayCount);
            if (!Enum.IsDefined(SourceScan))
            {
                throw new ArgumentOutOfRangeException(nameof(SourceScan));
            }
            switch (Unit)
            {
                case VideoPresentationUnit.Frame when FirstField is null:
                    break;
                case VideoPresentationUnit.Field when FirstField is VideoFieldParity parity && Enum.IsDefined(parity):
                    break;
                default:
                    throw new ArgumentException("The video presentation unit and field parity are inconsistent.");
            }
        }
    }
}
