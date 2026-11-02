// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Timing;

namespace Mediana
{
    /// <summary>PCM decoding with optional indexed seeking. Borrows the caller's source;
    /// returned frames survive session disposal.</summary>
    public class AudioSession : IAsyncDisposable
    {
        private readonly MediaDecodeSession<AudioFrame> state;

        internal AudioSession(MediaDecodeSession<AudioFrame> state) => this.state = state;

        public long AllocatedBufferBytes => state.AllocatedBufferBytes;
        public EncodedStreamInfo Stream => state.Stream;
        public bool CanSeek => state.CanSeek;
        public ValueTask<AudioFrame?> ReadFrameAsync(CancellationToken cancellationToken = default) => state.ReadFrameAsync(cancellationToken);

        /// <summary>Restarts at an output packet at or before target, reconstructing and suppressing required codec history.
        /// At the original stream beginning ordinary priming is preserved. Returned PCM retains its original sample grid;
        /// apply a time window for exact sample selection. Cancellation or failure requires disposal;
        /// packet and frame read budgets remain cumulative.</summary>
        public ValueTask SeekAsync(MediaTimestamp target, CancellationToken cancellationToken = default) =>
            state.SeekAsync(target, cancellationToken);

        public ValueTask DisposeAsync() => state.DisposeAsync();
    }
}
