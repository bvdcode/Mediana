// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Streaming.Hls
{
    /// <summary>Prepared mono/stereo Float32 audio for shared HLS encoders. Borrows its byte source.</summary>
    public class HlsAacPcmSource : IAsyncDisposable
    {
        private readonly PreparedAudioSource source;
        private HlsAacPcmSource(PreparedAudioSource source) => this.source = source;
        public AudioFormat Format => source.Format;
        public TimeBase TimeBase => source.TimeBase;

        public static async ValueTask<HlsAacPcmSource> OpenAsync(MediaEngine engine, IRandomAccessByteSource source, int streamIndex,
            int? channels, MediaBufferAllocator allocator, CancellationToken token = default) =>
            new(await PreparedAudioSource.OpenAsync(engine, source, allocator, new()
            {
                StreamIndex = streamIndex,
                Channels = channels,
                Input = new() { BufferBudgetBytes = allocator.LimitBytes }
            }, token).ConfigureAwait(false));

        public ValueTask<AudioFrame?> ReadFrameAsync(CancellationToken token) => source.ReadFrameAsync(token);
        public ValueTask DisposeAsync() => source.DisposeAsync();
    }
}
