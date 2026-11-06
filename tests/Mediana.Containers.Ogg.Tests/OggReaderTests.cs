// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text.Json;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Containers.Ogg.Tests
{
    public class OggReaderTests
    {
        [Theory]
        [InlineData("opus-stereo", 69, 65760)]
        [InlineData("vorbis-stereo", 61, 60417)]
        public async Task PublicIndexPreservesPayloadClocksMetadataCursorsAndPageCache(string name, int count, long duration)
        {
            byte[] data = await File.ReadAllBytesAsync(DataPath(name + ".ogg"));
            byte[] expected = await File.ReadAllBytesAsync(DataPath(name + ".packets"));
            using JsonDocument timing = JsonDocument.Parse(await File.ReadAllBytesAsync(DataPath(name + ".timing.json")));
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using OggTestSource source = new(data, 19);
            await using (OggReader reader = await OggReader.OpenAsync(source, allocator))
            {
                Assert.Equal("ogg", reader.Info.FormatId);
                Assert.Equal(duration, reader.Info.DeclaredDuration!.Value.Value);
                Assert.Single(reader.Streams);
                Assert.Equal(count, reader.GetPacketCount(0));
                Assert.Contains(reader.Info.StreamInfo![0].Tags!, tag => tag.Name == "title");
                Assert.Equal(0, source.Position);
                Assert.Equal(data.Length, source.BytesRead);
                using (MediaPacket last = await reader.ReadPacketAtAsync(0, count - 1))
                {
                    Assert.Equal(timing.RootElement[count - 1].GetProperty("pts").GetInt64(), last.PresentationTime!.Value.Value);
                }
                long cached = source.BytesRead;
                using (MediaPacket repeated = await reader.ReadPacketAtAsync(0, count - 1)) { Assert.NotEmpty(repeated.Data.ToArray()); }
                Assert.Equal(cached, source.BytesRead);
                long beforeQueries = source.BytesRead;
                ContainerSeekPoint seek = reader.FindSeekPoint(0, new(duration / 2, reader.Streams[0].TimeBase), count)!.Value;
                Assert.True(seek.PresentationTime.Value <= duration / 2);
                ContainerSeekPoint preroll = reader.GetPrerollStart(0, seek.PacketIndex, 1);
                Assert.True(preroll.PacketIndex < seek.PacketIndex);
                Assert.Equal(beforeQueries, source.BytesRead);
                int offset = 0;
                for (int index = 0; index < count; index++)
                {
                    using MediaPacket packet = (await reader.ReadPacketAsync())!;
                    int length = BinaryPrimitives.ReadInt32LittleEndian(expected.AsSpan(offset));
                    offset += 4;
                    Assert.Equal(expected.AsSpan(offset, length).ToArray(), packet.Data.ToArray());
                    offset += length;
                    Assert.Equal(timing.RootElement[index].GetProperty("pts").GetInt64(), packet.PresentationTime!.Value.Value);
                    Assert.Equal(packet.PresentationTime, packet.DecodeTime);
                    long presentation = reader.GetPresentationDuration(0, index)!.Value.Value;
                    if (index > 0 || name == "opus-stereo")
                    {
                        Assert.Equal(timing.RootElement[index].GetProperty("duration").GetInt64(), presentation);
                    }
                    else { Assert.Equal(0, presentation); }
                }
                Assert.Null(await reader.ReadPacketAsync());
                Assert.True(source.BytesRead < data.Length * 4L);
                Assert.Equal(0, source.Position);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.Disposed);
        }

        [Fact]
        public async Task OpusSeekPrerollIncludesAtLeastEightyMillisecondsAndClampsAtTheBeginning()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using OggTestSource source = new(await File.ReadAllBytesAsync(DataPath("opus-stereo.ogg")));
            await using OggReader reader = await OggReader.OpenAsync(source, allocator);
            ContainerSeekPoint start = reader.GetPrerollStart(0, 30, 0);
            Assert.Equal(26, start.PacketIndex);
            Assert.Equal(0, reader.GetPrerollStart(0, 1, 0).PacketIndex);
            Assert.Equal(20, reader.GetPrerollStart(0, 30, 10).PacketIndex);
            Assert.Equal(0, reader.FindSeekPoint(0, new(-9000, new(1, 48000)), 69)!.Value.PacketIndex);
            Assert.Null(reader.FindSeekPoint(0, new(0, new(1, 48000)), 0));
        }

        [Fact]
        public async Task IndexedPacketCanCrossPagesAndRetainedOutputOutlivesTheReader()
        {
            byte[][] packets = await ReadHeadersAndFirstAudioAsync();
            byte[] audio = packets[2];
            Assert.True(audio.Length > 255);
            byte[] data = [.. OggPacketTests.Page(2, 0, 91, 0, [(byte)packets[0].Length], packets[0]),
                .. OggPacketTests.Page(0, 0, 91, 1, [(byte)packets[1].Length], packets[1]),
                .. OggPacketTests.Page(0, -1, 91, 2, [255], audio[..255]),
                .. OggPacketTests.Page(5, 960, 91, 3, [(byte)(audio.Length - 255)], audio[255..])];
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using OggTestSource source = new(data, 11);
            MediaPacket retained;
            await using (OggReader reader = await OggReader.OpenAsync(source, allocator))
            {
                retained = await reader.ReadPacketAtAsync(0, 0);
                Assert.Equal(648, reader.Info.DeclaredDuration!.Value.Value);
                Assert.Equal(960, reader.GetPresentationDuration(0, 0)!.Value.Value);
                Assert.Equal(-312, retained.PresentationTime!.Value.Value);
            }
            Assert.Equal(audio, retained.Data.ToArray());
            retained.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task FailedAndCancelledIndexedReadsCanBeRetriedWithoutMovingTheSequentialCursor(bool cancel)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using OggTestSource source = new(await File.ReadAllBytesAsync(DataPath("opus-stereo.ogg")), 7);
            await using OggReader reader = await OggReader.OpenAsync(source, allocator);
            long retained = allocator.AllocatedBytes;
            using CancellationTokenSource cancellation = new();
            if (cancel)
            {
                source.AfterReadAsync = () => new ValueTask(cancellation.CancelAsync());
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadPacketAtAsync(0, 68, cancellation.Token).AsTask());
            }
            else
            {
                source.AfterRead = () => throw new IOException("An indexed read failed.");
                await Assert.ThrowsAsync<IOException>(() => reader.ReadPacketAtAsync(0, 68).AsTask());
            }
            source.AfterRead = null;
            source.AfterReadAsync = null;
            Assert.Equal(retained, allocator.AllocatedBytes);
            using MediaPacket packet = await reader.ReadPacketAtAsync(0, 68);
            using MediaPacket first = (await reader.ReadPacketAsync())!;
            Assert.Equal(-312, first.PresentationTime!.Value.Value);
        }

        [Fact]
        public async Task ChainedLinksAreExposedAsSeparateSelectableStreams()
        {
            byte[] data = await File.ReadAllBytesAsync(DataPath("opus-stereo.ogg"));
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using OggTestSource source = new([.. data, .. data]);
            await using (OggReader reader = await OggReader.OpenAsync(source, allocator))
            {
                Assert.Equal(2, reader.Streams.Count);
                Assert.Null(reader.Info.DeclaredDuration);
                Assert.Equal(69, reader.GetPacketCount(0));
                Assert.Equal(69, reader.GetPacketCount(1));
                using MediaPacket first = await reader.ReadPacketAtAsync(0, 68);
                using MediaPacket next = await reader.ReadPacketAtAsync(1, 68);
                Assert.Equal(first.Data.ToArray(), next.Data.ToArray());
                Assert.Equal(first.PresentationTime, next.PresentationTime);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task PublicReaderRejectsOverlappingQueriesAndDisposalDuringPacketIo()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using OggTestSource source = new(await File.ReadAllBytesAsync(DataPath("opus-stereo.ogg")));
            await using OggReader reader = await OggReader.OpenAsync(source, allocator);
            TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            source.Gate = gate;
            Task<MediaPacket> active = reader.ReadPacketAtAsync(0, 68).AsTask();
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Throws<InvalidOperationException>(() => reader.GetPacketCount(0));
            await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadPacketAsync().AsTask());
            await Assert.ThrowsAsync<InvalidOperationException>(() => reader.DisposeAsync().AsTask());
            gate.SetResult();
            using MediaPacket packet = await active;
            source.Gate = null;
            Assert.Equal(69, reader.GetPacketCount(0));
        }

        [Fact]
        public async Task InvalidFinalGranuleAndInsufficientAllocationBudgetReleaseAllIndexStorage()
        {
            byte[][] packets = await ReadHeadersAndFirstAudioAsync();
            byte[] audio = packets[2];
            byte[] lacing = audio.Length < 255 ? [(byte)audio.Length] : [255, (byte)(audio.Length - 255)];
            byte[] data = [.. OggPacketTests.Page(2, 0, 1, 0, [(byte)packets[0].Length], packets[0]),
                .. OggPacketTests.Page(0, 0, 1, 1, [(byte)packets[1].Length], packets[1]),
                .. OggPacketTests.Page(4, 1000, 1, 2, lacing, audio)];
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using OggTestSource invalid = new(data);
            await Assert.ThrowsAsync<InvalidDataException>(() => OggReader.OpenAsync(invalid, allocator).AsTask());
            Assert.Equal(0, allocator.AllocatedBytes);
            MediaBufferAllocator small = new(500);
            await using OggTestSource valid = new(await File.ReadAllBytesAsync(DataPath("opus-stereo.ogg")));
            await Assert.ThrowsAsync<MediaMemoryLimitException>(() => OggReader.OpenAsync(valid, small).AsTask());
            Assert.Equal(0, small.AllocatedBytes);
            Assert.False(valid.Disposed);
        }

        private static async Task<byte[][]> ReadHeadersAndFirstAudioAsync()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using OggTestSource source = new(await File.ReadAllBytesAsync(DataPath("opus-stereo.ogg")));
            await using OggPacketReader reader = new(source, allocator);
            byte[][] result = new byte[3][];
            for (int index = 0; index < 3; index++)
            {
                using OggPacket packet = (await reader.ReadPacketAsync())!;
                result[index] = packet.Data.ToArray();
            }
            return result;
        }

        private static string DataPath(string name) => Path.Combine(AppContext.BaseDirectory, "Data", name);
    }
}
