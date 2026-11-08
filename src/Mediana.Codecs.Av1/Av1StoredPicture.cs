// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Codecs.Av1
{
    internal class Av1StoredPicture(Av1Picture picture, MediaBuffer cdfs, MediaBuffer? motionField = null, int alternateOrderHint = 0, MediaBuffer? segmentMap = null) : IDisposable
    {
        internal Av1FrameHeader Header { get; } = picture.Header;
        internal Av1SequenceHeader PixelGeometry => Header.PixelGeometry;
        internal MediaBuffer Pixels { get; } = picture.Pixels.Retain();
        internal MediaBuffer Cdfs { get; } = cdfs.Retain();
        internal MediaBuffer? MotionField { get; } = motionField?.Retain();
        internal MediaBuffer? SegmentMap { get; } = segmentMap?.Retain();
        internal int AlternateOrderHint { get; } = alternateOrderHint;
        internal bool Showable { get; set; } = picture.Header.Showable;
        public void Dispose()
        {
            try { Pixels.Dispose(); }
            finally
            {
                try { Cdfs.Dispose(); }
                finally
                {
                    try { MotionField?.Dispose(); }
                    finally { SegmentMap?.Dispose(); }
                }
            }
        }
    }
}
