using Mediana.Containers;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Execution;
using Mediana.IO;

namespace Mediana
{
    /// <summary>Forward-only video decoding. Owns container/decoder state but not the caller's source.
    /// Returned frames survive session disposal until their owners release them.</summary>
    public class VideoSession : IAsyncDisposable
    {
        private readonly IContainerReader reader;
        private readonly IByteSource replay;
        private readonly VideoDecodePipeline pipeline;
        private readonly MediaBufferAllocator allocator;
        private readonly long maxFrameReads;
        private long frameReads;
        private bool disposed;
        private bool faulted;
        private int busy;

        internal VideoSession(IContainerReader reader, IByteSource replay, VideoDecodePipeline pipeline,
            MediaBufferAllocator allocator, long maxFrameReads)
        {
            this.reader = reader;
            this.replay = replay;
            this.pipeline = pipeline;
            this.allocator = allocator;
            this.maxFrameReads = maxFrameReads;
        }

        public long AllocatedBufferBytes => allocator.AllocatedBytes;

        public async ValueTask<VideoFrame?> ReadFrameAsync(CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                return await ReadCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }

        /// <summary>Consumes remaining frames until the first PTS at or after target. Does not seek or return a preceding frame.
        /// Fails if a frame has no PTS. Null means end of stream; no substitution is made.</summary>
        public async ValueTask<VideoFrame?> ReadFrameAtOrAfterAsync(MediaTimestamp target,
            CancellationToken cancellationToken = default)
        {
            target.TimeBase.Validate();
            Enter();
            try
            {
                while (true)
                {
                    VideoFrame? frame = await ReadCoreAsync(cancellationToken).ConfigureAwait(false);
                    if (frame is null)
                    {
                        return null;
                    }
                    bool selected = false;
                    try
                    {
                        MediaTimestamp timestamp = frame.Timestamp
                            ?? throw new NotSupportedException("Frame selection requires presentation timestamps.");
                        if (timestamp.CompareTo(target) >= 0)
                        {
                            selected = true;
                            return frame;
                        }
                    }
                    finally
                    {
                        if (!selected)
                        {
                            frame.Dispose();
                        }
                    }
                }
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }

        private async ValueTask<VideoFrame?> ReadCoreAsync(CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted)
            {
                throw new InvalidOperationException("The session has failed and must be disposed.");
            }
            bool succeeded = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (frameReads == maxFrameReads)
                {
                    throw new InvalidOperationException("The session frame read limit has been reached.");
                }
                frameReads++;
                VideoFrame? frame = await pipeline.ReadAsync(cancellationToken).ConfigureAwait(false);
                succeeded = true;
                return frame;
            }
            finally
            {
                if (!succeeded)
                {
                    faulted = true;
                    pipeline.Dispose();
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            Enter();
            try
            {
                if (!disposed)
                {
                    disposed = true;
                    try
                    {
                        pipeline.Dispose();
                    }
                    finally
                    {
                        try
                        {
                            await reader.DisposeAsync().ConfigureAwait(false);
                        }
                        finally
                        {
                            await replay.DisposeAsync().ConfigureAwait(false);
                        }
                    }
                }
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
            GC.SuppressFinalize(this);
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("Concurrent session operations are not supported.");
            }
        }
    }
}
