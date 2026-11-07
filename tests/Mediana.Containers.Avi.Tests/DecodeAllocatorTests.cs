// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers;
using Mediana.Codecs.Jpeg;
using Mediana.Codecs.Pcm;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Containers.Avi.Tests
{
    public class DecodeAllocatorTests
    {
        [Fact]
        public async Task AudioAndVideoSessionsShareTheCallerPoolAndRetainedFramesOutliveBothSessions()
        {
            byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "mjpeg-pcm.avi"));
            await using AviTestSource source = new(bytes);
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new AviReaderFactory()).AddVideoDecoder(new MjpegDecoderFactory())
                .AddAudioDecoder(new PcmDecoderFactory()).Build();
            MediaBufferAllocator allocator = new(1024 * 1024, MemoryPool<byte>.Shared);
            await using VideoSession video = await engine.OpenVideoAsync(source, allocator, new(), null);
            await using AudioSession audio = await engine.OpenAudioAsync(source, allocator, new(), 1);
            using VideoFrame picture = (await video.ReadFrameAsync())!;
            using AudioFrame samples = (await audio.ReadFrameAsync())!;
            Assert.Equal(video.AllocatedBufferBytes + audio.AllocatedBufferBytes, allocator.AllocatedBytes);
            byte[] expected = picture.Data.ToArray();
            await video.DisposeAsync(); await audio.DisposeAsync();
            Assert.True(allocator.AllocatedBytes > 0);
            Assert.Equal(expected, picture.Data.ToArray());
            Assert.False(source.Disposed);
            picture.Dispose(); samples.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task MissingDecoderAndInputScopeFailureReturnAllSharedBudget()
        {
            byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "mjpeg-pcm.avi"));
            await using AviTestSource source = new(bytes);
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new AviReaderFactory()).Build();
            MediaBufferAllocator allocator = new(1024 * 1024, MemoryPool<byte>.Shared);
            await Assert.ThrowsAsync<NotSupportedException>(async () => await engine.OpenVideoAsync(source, allocator, new(), 0));
            Assert.Equal(0, allocator.AllocatedBytes);
            await Assert.ThrowsAsync<MediaMemoryLimitException>(async () =>
                await engine.OpenAudioAsync(source, allocator, new() { BufferBudgetBytes = 4096 }, null));
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.Disposed);
        }
    }
}
