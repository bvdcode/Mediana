using Mediana.Core.Video;

namespace Mediana.Codecs.H264
{
    internal class H264FrameQueue : IDisposable
    {
        private readonly SortedDictionary<int, VideoFrame> pending = [];
        private readonly Queue<VideoFrame> ready = [];
        private int? lastOutputOrder;
        private bool disposed;

        public bool HasReady => ready.Count != 0;
        public int PendingCount => pending.Count;

        /// <summary>Transfers frame ownership on successful return.</summary>
        public void Add(VideoFrame frame, H264PictureOrder order, H264SliceHeader header)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (HasReady)
            {
                throw new InvalidOperationException("H.264 output must be received before adding another frame.");
            }
            H264PictureBufferLimits limits = H264PictureBufferLimits.Read(header.Sequence);
            bool reset = header.IsIdr || header.MemoryOperations.Any(operation => operation.Operation == 5);
            if (reset)
            {
                if (header.IsIdr && header.NoOutputOfPriorPictures)
                {
                    foreach (VideoFrame previous in pending.Values)
                    {
                        previous.Dispose();
                    }
                    pending.Clear();
                }
                else
                {
                    Drain();
                }
                lastOutputOrder = null;
            }
            if ((lastOutputOrder is not null && order.Value <= lastOutputOrder) || pending.ContainsKey(order.Value))
            {
                throw new InvalidDataException("H.264 picture order is duplicated or exceeds the permitted reordering delay.");
            }
            pending.Add(order.Value, frame);
            while (pending.Count > limits.ReorderFrames)
            {
                MoveFirst();
            }
        }

        public VideoFrame? Receive()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return ready.TryDequeue(out VideoFrame? frame) ? frame : null;
        }

        public void Drain()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            while (pending.Count != 0)
            {
                MoveFirst();
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            foreach (VideoFrame frame in pending.Values)
            {
                frame.Dispose();
            }
            pending.Clear();
            while (ready.TryDequeue(out VideoFrame? frame))
            {
                frame.Dispose();
            }
            lastOutputOrder = null;
            disposed = true;
        }

        private void MoveFirst()
        {
            KeyValuePair<int, VideoFrame> first = pending.First();
            ready.Enqueue(first.Value);
            pending.Remove(first.Key);
            lastOutputOrder = first.Key;
        }
    }
}
