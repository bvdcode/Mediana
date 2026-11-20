// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Pcm;
using Mediana.Containers.Avi;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Tests
{
    public class ContainerIndexedSessionTests
    {
        [Fact]
        public async Task IndexedReadsPreserveSequentialCursorAndShareReadBudget()
        {
            MediaEngine engine = Engine();
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            await using TrackingRandomAccessSource source = new(await File.ReadAllBytesAsync(Data()));
            MediaPacket retained;
            await using (ContainerSession container = await engine.OpenContainerAsync(source, allocator, new() { MaxPacketReads = 2 }))
            {
                source.Reads.Clear();
                Assert.True(container.CanReadPacketsAt);
                Assert.True(container.GetPacketCount(1) > 0);
                Assert.Null(container.GetPresentationRange(1));
                Assert.Empty(source.Reads);
                await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => container.ReadPacketAtAsync(1, -1).AsTask());
                retained = await container.ReadPacketAtAsync(1, 0);
                Assert.Equal(1, retained.StreamIndex);
                using MediaPacket first = Assert.IsType<MediaPacket>(await container.ReadPacketAsync());
                Assert.Equal(0, first.StreamIndex);
                Assert.Throws<InvalidOperationException>(() => engine.OpenAudio(container, 1));
                await Assert.ThrowsAsync<InvalidOperationException>(() => container.ReadPacketAtAsync(1, 1).AsTask());
                await Assert.ThrowsAsync<InvalidOperationException>(() => container.ReadPacketAsync().AsTask());
            }
            Assert.Equal(2048, retained.Data.Length);
            Assert.True(allocator.AllocatedBytes > 0);
            retained.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.IsDisposed);
        }

        [Fact]
        public async Task MetadataQueriesAllowTransferAndCancellationFaultsStartedReads()
        {
            MediaEngine engine = Engine();
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Data()));
            await using (ContainerSession container = await engine.OpenContainerAsync(source, allocator, new()))
            {
                Assert.Null(container.GetPresentationRange(1));
                Assert.Throws<ArgumentOutOfRangeException>(() => container.GetPresentationRange(7));
                await using AudioSession audio = engine.OpenAudio(container, 1);
                using AudioFrame first = Assert.IsType<AudioFrame>(await audio.ReadFrameAsync());
                Assert.Equal(1024, first.SamplesPerChannel);
                Assert.Throws<ObjectDisposedException>(() => container.GetPacketCount(1));
            }
            await using (ContainerSession container = await engine.OpenContainerAsync(source, allocator, new()))
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => container.ReadPacketAtAsync(1, 0, new(true)).AsTask());
                Assert.Throws<InvalidOperationException>(() => container.GetPacketCount(1));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static MediaEngine Engine() => new MediaEngineBuilder().AddContainer(new AviReaderFactory())
            .AddAudioDecoder(new PcmDecoderFactory()).Build();
        private static string Data() => Path.Combine(AppContext.BaseDirectory, "Data", "Avi", "mjpeg-pcm.avi");
    }
}
