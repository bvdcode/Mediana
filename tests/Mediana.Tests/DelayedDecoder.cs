using Mediana.Codecs;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    internal class DelayedDecoder(MediaBufferAllocator allocator) : IMediaCodec<MediaPacket, VideoFrame>
    {
        private readonly Queue<MediaPacket> packets = [];
        private bool completed;
        public bool IsDisposed { get; private set; }
        public int PendingCount => packets.Count;

        public CodecInputStatus TrySend(MediaPacket input, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (completed)
            {
                throw new InvalidOperationException("Input is completed.");
            }
            if (packets.Count >= 2)
            {
                return CodecInputStatus.OutputRequired;
            }
            packets.Enqueue(input.Retain());
            return CodecInputStatus.Accepted;
        }

        public CodecOutput<VideoFrame> Receive(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (packets.Count > 1 || (completed && packets.Count > 0))
            {
                using MediaPacket packet = packets.Dequeue();
                return CodecOutput<VideoFrame>.Available(FrameFixture.Create(allocator, packet.Data.Span[0],
                    packet.PresentationTime!.Value.Value));
            }
            if (completed)
            {
                return CodecOutput<VideoFrame>.EndOfStream;
            }
            return CodecOutput<VideoFrame>.InputRequired;
        }

        public void CompleteInput() => completed = true;

        public void Reset()
        {
            while (packets.TryDequeue(out MediaPacket? packet))
            {
                packet.Dispose();
            }
            completed = false;
        }

        public void Dispose()
        {
            IsDisposed = true;
            Reset();
        }
    }
}
