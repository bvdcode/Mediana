// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.H264
{
    internal class H264FrameQueue(MediaBufferAllocator allocator) : IDisposable
    {
        private readonly VideoFrameReorderQueue frames = new();
        private H264QueuedField? pendingField;
        private bool disposed;
        public bool HasReady => frames.HasReady;
        public int PendingCount => frames.PendingCount + (pendingField is null ? 0 : 1);

        public void Add(VideoFrame frame, H264PictureOrder order, H264SliceHeader header, CancellationToken token = default)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            token.ThrowIfCancellationRequested();
            if (HasReady) { throw new InvalidOperationException("H.264 output must be received before adding another frame."); }
            if (header.Layout.IsField && pendingField is not null && pendingField.Complements(header))
            {
                VideoFrame combined = H264FieldFramePublisher.Combine(pendingField, frame, order, allocator, token);
                bool queued = false;
                try
                {
                    H264PictureOrder pairOrder = header.Structure switch
                    {
                        H264PictureStructure.TopField => new(order.Top, pendingField.Order.Bottom),
                        H264PictureStructure.BottomField => new(pendingField.Order.Top, order.Bottom),
                        _ => throw new InvalidOperationException("A complementary field must have a field structure.")
                    };
                    Enqueue(combined, pairOrder, pendingField.Header);
                    queued = true;
                }
                finally
                {
                    if (!queued) { combined.Dispose(); }
                }
                pendingField.Dispose();
                pendingField = null;
                frame.Dispose();
                return;
            }
            FlushField(header.IsIdr && header.NoOutputOfPriorPictures);
            if (header.Layout.IsField)
            {
                pendingField = new(frame, order, header);
            }
            else
            {
                Enqueue(frame, order, header);
            }
        }

        public void AddDiscarded(H264PictureOrder order, H264SliceHeader header, CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            token.ThrowIfCancellationRequested();
            if (HasReady || header.Layout.IsField || header.IsIdr || H264FieldPairing.ResetsReferences(header))
            {
                throw new InvalidOperationException("The H.264 picture cannot be discarded at this point.");
            }
            FlushField(false);
            frames.AddDiscarded(order.Value, H264PictureBufferLimits.Read(header.Sequence).ReorderFrames);
        }

        private void Enqueue(VideoFrame frame, H264PictureOrder order, H264SliceHeader header)
        {
            H264PictureBufferLimits limits = H264PictureBufferLimits.Read(header.Sequence);
            if (header.IsIdr || H264FieldPairing.ResetsReferences(header))
            {
                frames.BeginSequence(header.IsIdr && header.NoOutputOfPriorPictures);
            }
            frames.Add(frame, order.Value, limits.ReorderFrames);
        }

        private void FlushField(bool discard)
        {
            if (pendingField is null) { return; }
            if (discard)
            {
                pendingField.Dispose();
            }
            else
            {
                Enqueue(pendingField.Frame, pendingField.Order, pendingField.Header);
            }
            pendingField = null;
        }

        public VideoFrame? Receive() => frames.Receive();

        public void Drain()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            FlushField(false);
            frames.Drain();
        }

        public void Dispose()
        {
            if (disposed) { return; }
            disposed = true;
            try
            {
                pendingField?.Dispose();
            }
            finally
            {
                pendingField = null;
                frames.Dispose();
            }
        }
    }
}
