// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Matroska;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.IO;
using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    public class HlsContainerAudioTests
    {
        [Fact]
        public Task AviPcmUsesRegisteredContainerWithoutChangingSamplesOrTimestamps() =>
            HlsContainerAudioTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Avi"));

        [Fact]
        public async Task MatroskaAudioCopyReadsOnlySelectedIndexedPackets()
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new MatroskaReaderFactory()).Build();
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            string path = Path.Combine(AppContext.BaseDirectory, "Data", "Matroska", "avc-aac-attachment.mkv");
            await using TrackingRandomAccessSource source = new(await File.ReadAllBytesAsync(path));
            await using (HlsCopyAudioSource audio = await HlsCopyAudioSource.OpenAsync(engine, source, 1, allocator))
            {
                CollectingPacketWriter writer = new();
                await audio.WriteBeforeAsync(new(500, new(1, 1000)), [writer], default);
                int firstCount = writer.Packets.Count;
                await audio.WriteBeforeAsync(null, [writer], default);
                await using ContainerSession reference = await engine.OpenContainerAsync(source, allocator, new());
                Assert.Equal(reference.GetPacketCount(1), audio.PacketsWritten);
                Assert.True(firstCount > 0 && firstCount < writer.Packets.Count);
                for (int index = 0; index < writer.Packets.Count; index++)
                {
                    using MediaPacket packet = await reference.ReadPacketAtAsync(1, index);
                    Assert.Equal(packet.Data.ToArray(), writer.Packets[index]);
                    Assert.Equal(index < firstCount, packet.PresentationTime!.Value.CompareTo(new(500, new(1, 1000))) < 0);
                }
                Assert.False(writer.IsDisposed);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.IsDisposed);
        }
    }
}
