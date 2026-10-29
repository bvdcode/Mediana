using Mediana.Core;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana
{
    /// <summary>Forward-only video decoding. Borrows the caller's source; returned frames have independent ownership.</summary>
    public class VideoSession : IAsyncDisposable
    {
        private readonly MediaDecodeSession<VideoFrame> state;

        internal VideoSession(MediaDecodeSession<VideoFrame> state) => this.state = state;

        public long AllocatedBufferBytes => state.AllocatedBufferBytes;
        public EncodedStreamInfo Stream => state.Stream;
        public ValueTask<VideoFrame?> ReadFrameAsync(CancellationToken cancellationToken = default) => state.ReadFrameAsync(cancellationToken);

        /// <summary>Consumes remaining frames until the first PTS at or after target. Does not seek or return a preceding frame.
        /// Fails if a frame has no PTS. Null means end of stream; no substitution is made.</summary>
        public async ValueTask<VideoFrame?> ReadFrameAtOrAfterAsync(MediaTimestamp target,
            CancellationToken cancellationToken = default)
        {
            target.TimeBase.Validate();
            state.Enter();
            try
            {
                while (true)
                {
                    VideoFrame? frame = await state.ReadCoreAsync(cancellationToken).ConfigureAwait(false);
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
                state.Exit();
            }
        }

        public ValueTask DisposeAsync() => state.DisposeAsync();
    }
}
