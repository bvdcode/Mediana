// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Audio;

namespace Mediana
{
    /// <summary>Forward-only PCM output. Borrows the caller's source; returned frames survive session disposal.</summary>
    public class AudioSession : IAsyncDisposable
    {
        private readonly MediaDecodeSession<AudioFrame> state;

        internal AudioSession(MediaDecodeSession<AudioFrame> state) => this.state = state;

        public long AllocatedBufferBytes => state.AllocatedBufferBytes;
        public EncodedStreamInfo Stream => state.Stream;
        public ValueTask<AudioFrame?> ReadFrameAsync(CancellationToken cancellationToken = default) => state.ReadFrameAsync(cancellationToken);
        public ValueTask DisposeAsync() => state.DisposeAsync();
    }
}
