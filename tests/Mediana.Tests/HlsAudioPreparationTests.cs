// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Aac;
using Mediana.Containers.Mp4;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    public class HlsAudioPreparationTests
    {
        [Fact]
        public Task PublicPcmSourceUsesTheOpenedReaderAndKeepsCallerAllocationsAlive() =>
            HlsAudioPreparationTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Aac"));

        [Fact]
        public async Task FailedDecoderAdoptionLeavesTheReaderAvailableForAnotherEngine()
        {
            await using TrackingRandomAccessSource source = new(await File.ReadAllBytesAsync(Data()));
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            MediaEngine missing = new MediaEngineBuilder().Build();
            Assert.Throws<NotSupportedException>(() => missing.OpenAudio(reader, allocator, 0));
            MediaEngine supported = new MediaEngineBuilder().AddAudioDecoder(new AacDecoderFactory()).Build();
            await using (AudioSession session = supported.OpenAudio(reader, allocator, 0))
            {
                using AudioFrame? frame = await session.ReadFrameAsync();
                Assert.NotNull(frame);
            }
            Assert.False(source.IsDisposed);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static string Data() => Path.Combine(AppContext.BaseDirectory, "Data", "Aac", "lc-mono-48.m4a");
    }
}
