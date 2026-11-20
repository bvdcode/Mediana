// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Tests
{
    public class H264PresentationRecoveryTests
    {
        private static string Root => Path.Combine(AppContext.BaseDirectory, "Data", "Metadata");

        [Fact]
        public Task RecoveryPreservesIndependentPixelsPacketTimingSeekingAndOwnership() =>
            H264PresentationRecoveryTestInput.VerifyAsync(Root);

        [Fact]
        public async Task DefaultDecoderRejectsContradictoryHintsWithoutWarningsOrRetainedBuffers()
        {
            List<string> warnings = [];
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory())
                .AddVideoDecoder(new H264DecoderFactory(null, warnings.Add)).Build();
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(Root, "avc-interlaced.mp4")));
            await using VideoSession session = await engine.OpenVideoAsync(source, allocator, new(), 0);
            InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() => session.ReadFrameAsync().AsTask());
            Assert.Contains("picture-timing", error.Message, StringComparison.Ordinal);
            Assert.Empty(warnings);
            await session.DisposeAsync();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task WarningCallbackFailuresArePropagatedAndOwnedBuffersAreReleased()
        {
            InvalidOperationException expected = new("The warning consumer failed.");
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory())
                .AddVideoDecoder(new H264DecoderFactory(new() { IgnoreInconsistentPictureTiming = true }, _ => throw expected)).Build();
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(Root, "avc-interlaced.mp4")));
            await using VideoSession session = await engine.OpenVideoAsync(source, allocator, new(), 0);
            Assert.Same(expected, await Assert.ThrowsAsync<InvalidOperationException>(() => session.ReadFrameAsync().AsTask()));
            await session.DisposeAsync();
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
