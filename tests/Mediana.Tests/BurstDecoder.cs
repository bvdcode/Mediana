using Mediana.Codecs;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    internal class BurstDecoder(MediaBufferAllocator allocator) : IMediaCodec<MediaPacket, VideoFrame>
    {
        private readonly Queue<VideoFrame> frames = [];
        private bool completed;
        public bool RefuseInput { get; init; }
        public bool RequestInputAfterCompletion { get; init; }
        public bool IsDisposed { get; private set; }

        public CodecInputStatus TrySend(MediaPacket input, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (RefuseInput || frames.Count > 0)
            {
                return CodecInputStatus.OutputRequired;
            }
            frames.Enqueue(FrameFixture.Create(allocator, input.Data.Span[0], 0));
            frames.Enqueue(FrameFixture.Create(allocator, input.Data.Span[0], 1));
            return CodecInputStatus.Accepted;
        }

        public CodecOutput<VideoFrame> Receive(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (frames.TryDequeue(out VideoFrame? frame))
            {
                return CodecOutput<VideoFrame>.Available(frame);
            }
            if (completed && !RequestInputAfterCompletion)
            {
                return CodecOutput<VideoFrame>.EndOfStream;
            }
            return CodecOutput<VideoFrame>.InputRequired;
        }

        public void CompleteInput() => completed = true;

        public void Reset()
        {
            while (frames.TryDequeue(out VideoFrame? frame))
            {
                frame.Dispose();
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
