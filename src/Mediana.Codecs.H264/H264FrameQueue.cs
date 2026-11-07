// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Codecs.H264
{
    internal class H264FrameQueue : IDisposable
    {
        private readonly VideoFrameReorderQueue frames = new();
        public bool HasReady => frames.HasReady;
        public int PendingCount => frames.PendingCount;

        public void Add(VideoFrame frame, H264PictureOrder order, H264SliceHeader header)
        {
            if (HasReady) { throw new InvalidOperationException("H.264 output must be received before adding another frame."); }
            H264PictureBufferLimits limits = H264PictureBufferLimits.Read(header.Sequence);
            if (header.IsIdr || header.MemoryOperations.Any(operation => operation.Operation == 5))
            { frames.BeginSequence(header.IsIdr && header.NoOutputOfPriorPictures); }
            frames.Add(frame, order.Value, limits.ReorderFrames);
        }

        public VideoFrame? Receive() => frames.Receive();
        public void Drain() => frames.Drain();
        public void Dispose() => frames.Dispose();
    }
}
