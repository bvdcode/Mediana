// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Codecs
{
    /// <summary>Owns decoded output leases until their display order is available.</summary>
    internal class VideoFrameReorderQueue : IDisposable
    {
        private readonly SortedDictionary<long, VideoFrame?> pending = [];
        private readonly Queue<VideoFrame> ready = [];
        private long? lastOrder;
        private bool disposed;
        public bool HasReady => ready.Count != 0;
        public int PendingCount => pending.Count;

        public void Add(VideoFrame frame, long order, int maximumReorder)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ArgumentNullException.ThrowIfNull(frame);
            AddEntry(frame, order, maximumReorder);
        }

        public void AddDiscarded(long order, int maximumReorder)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            AddEntry(null, order, maximumReorder);
        }

        private void AddEntry(VideoFrame? frame, long order, int maximumReorder)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(maximumReorder);
            if (lastOrder is not null && order <= lastOrder || pending.ContainsKey(order))
            { throw new InvalidDataException("Video picture order is duplicated or exceeds the permitted reordering delay."); }
            pending.Add(order, frame);
            while (pending.Count > maximumReorder) { MoveFirst(); }
        }

        public void BeginSequence(bool discardPending)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (discardPending)
            {
                foreach (VideoFrame? frame in pending.Values) { frame?.Dispose(); }
                pending.Clear();
            }
            else { Drain(); }
            lastOrder = null;
        }

        public VideoFrame? Receive()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return ready.TryDequeue(out VideoFrame? frame) ? frame : null;
        }

        public void Drain()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            while (pending.Count != 0) { MoveFirst(); }
        }

        private void MoveFirst()
        {
            KeyValuePair<long, VideoFrame?> entry = pending.First();
            if (entry.Value is VideoFrame frame)
            {
                ready.Enqueue(frame);
            }
            pending.Remove(entry.Key);
            lastOrder = entry.Key;
        }

        public void Dispose()
        {
            if (disposed) { return; }
            foreach (VideoFrame? frame in pending.Values) { frame?.Dispose(); }
            pending.Clear();
            while (ready.TryDequeue(out VideoFrame? frame)) { frame.Dispose(); }
            disposed = true;
        }
    }
}
