// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class Mp4FragmentValidationTests
    {
        [Theory]
        [InlineData("mfhd", 0, 0x01000000u)]
        [InlineData("tfhd", 0, 0x00800000u)]
        [InlineData("tfdt", 0, 0x02000000u)]
        [InlineData("trun", 0, 0x00800000u)]
        public async Task UnsupportedFragmentVersionsAndFlagsReleaseStorage(string type, int field, uint value)
        {
            byte[] data = await Mp4TestData.ReadAsync("fragment-relative.mp4");
            Mp4TestData.SetUInt32(data, type, field, value);
            await VerifyFailureAsync<NotSupportedException>(data);
        }

        [Theory]
        [InlineData("trex", 4, 900u)]
        [InlineData("tfhd", 4, 900u)]
        [InlineData("trun", 8, uint.MaxValue)]
        public async Task InvalidTrackIdentifiersAndMediaOffsetsReleaseStorage(string type, int field, uint value)
        {
            byte[] data = await Mp4TestData.ReadAsync("fragment-relative.mp4");
            Mp4TestData.SetUInt32(data, type, field, value);
            await VerifyFailureAsync<InvalidDataException>(data);
        }

        [Fact]
        public async Task MetadataAndSampleLimitsApplyToAllFragments()
        {
            byte[] data = await Mp4TestData.ReadAsync("fragment-relative.mp4");
            int movieBytes = Mp4TestBoxes.Get(data, "moov").Length;
            await VerifyFailureAsync<NotSupportedException>(data, new() { MaxMetadataBytes = movieBytes });
            await VerifyFailureAsync<NotSupportedException>(data, new() { MaxSamplesPerTrack = 20 });
            await VerifyFailureAsync<MediaMemoryLimitException>(data, memoryBudget: 32);
        }

        [Fact]
        public async Task RepeatedSequencesAndOverlappingDecodeTimesAreRejected()
        {
            byte[] original = await Mp4TestData.ReadAsync("fragment-relative.mp4");
            byte[] data = original.ToArray();
            int first = Mp4TestData.PayloadOffset(data, "mfhd");
            int second = data.AsSpan(first).IndexOf("mfhd"u8) + first + 4;
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(second + 4), 1);
            await VerifyFailureAsync<InvalidDataException>(data);
            data = original.ToArray();
            int nextFragment = data.AsSpan(Mp4TestData.PayloadOffset(data, "moof")).IndexOf("moof"u8) + Mp4TestData.PayloadOffset(data, "moof") + 4;
            int decode = data.AsSpan(nextFragment).IndexOf("tfdt"u8) + nextFragment + 4;
            BinaryPrimitives.WriteUInt64BigEndian(data.AsSpan(decode + 4), 0);
            await VerifyFailureAsync<InvalidDataException>(data);
        }

        [Fact]
        public async Task CancellationDuringHeaderAndPacketReadsReleasesBuffersAndKeepsPacketPosition()
        {
            byte[] data = await Mp4TestData.ReadAsync("fragment-relative.mp4");
            MediaBufferAllocator allocator = new(1024 * 1024);
            using CancellationTokenSource cancellation = new();
            await using TrackingRandomAccessSource source = new(data, 5) { AfterRead = cancellation.Cancel };
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Mp4Reader.OpenAsync(source, allocator, cancellationToken: cancellation.Token).AsTask());
            Assert.Equal(0, allocator.AllocatedBytes);
            source.AfterRead = null;
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            long metadataBytes = allocator.AllocatedBytes;
            using CancellationTokenSource packetCancellation = new();
            source.AfterRead = packetCancellation.Cancel;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadPacketAsync(packetCancellation.Token).AsTask());
            Assert.Equal(metadataBytes, allocator.AllocatedBytes);
            source.AfterRead = null;
            using MediaPacket packet = (await reader.ReadPacketAsync())!;
            Assert.Equal(reader.GetSampleInfo(packet.StreamIndex, 0).Size, packet.Data.Length);
        }

        [Fact]
        public async Task MissingDecodeTimeBoxesInheritThePreviousFragmentEnd()
        {
            byte[] original = await Mp4TestData.ReadAsync("fragment-relative.mp4");
            byte[] modified = original.ToArray();
            foreach (ReadOnlyMemory<byte> movie in Mp4TestBoxes.GetAll(modified, "moof"))
            {
                foreach (ReadOnlyMemory<byte> track in Mp4TestBoxes.GetAll(movie, "traf"))
                {
                    ReadOnlyMemory<byte> decodeTime = Mp4TestBoxes.Get(track, "tfdt");
                    Assert.True(MemoryMarshal.TryGetArray(decodeTime, out ArraySegment<byte> segment));
                    "free"u8.CopyTo(modified.AsSpan(segment.Offset - 4, 4));
                }
            }
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource expectedSource = new(original);
            await using TrackingRandomAccessSource actualSource = new(modified);
            await using Mp4Reader expected = await Mp4Reader.OpenAsync(expectedSource, allocator);
            await using Mp4Reader actual = await Mp4Reader.OpenAsync(actualSource, allocator);
            for (int stream = 0; stream < expected.Tracks.Count; stream++)
            {
                for (int index = 0; index < expected.Tracks[stream].SampleCount; index++)
                {
                    Assert.Equal(expected.GetSampleInfo(stream, index), actual.GetSampleInfo(stream, index));
                }
            }
        }

        [Fact]
        public async Task InitializationOnlyExposesDeclarationsAndEmptyPacketIndices()
        {
            byte[] data = await Mp4TestData.ReadAsync("fragment-relative.mp4");
            int firstFragment = Mp4TestData.PayloadOffset(data, "moof") - 8;
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(data[..firstFragment]);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            Assert.Equal(2, reader.Streams.Count);
            Assert.Equal("h264", reader.Streams[0].CodecId);
            Assert.Equal("aac", reader.Streams[1].CodecId);
            Assert.All(reader.Tracks, track => Assert.Equal(0, track.SampleCount));
            Assert.Null(await reader.ReadPacketAsync());
            Assert.Null(reader.FindSeekPoint(0, new(0, reader.Streams[0].TimeBase), 0));
            await reader.DisposeAsync();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task DecodeTimeVersionZeroPreservesExtendedOrEmptyIntervals(bool emptyInterval)
        {
            byte[] fixture = await Mp4TestData.ReadAsync("fragment-relative.mp4");
            int initialLength = Mp4TestData.PayloadOffset(fixture, "moof") - 8;
            byte[] first = Fragment(1, 0, 2, 40, false);
            byte[] gap = emptyInterval ? Fragment(2, 80, 0, 20, true) : [];
            byte[] last = Fragment(3, 100, 1, 40, false);
            byte[] input = [.. fixture.AsSpan(0, initialLength), .. first, .. gap, .. last];
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(input);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            Assert.Equal(3, reader.GetPacketCount(0));
            Assert.Equal(0, reader.GetSampleInfo(0, 0).DecodeTime.Value);
            Assert.Equal(40, reader.GetSampleInfo(0, 1).DecodeTime.Value);
            Assert.Equal(100, reader.GetSampleInfo(0, 2).DecodeTime.Value);
            Assert.Equal(emptyInterval ? 40 : 60, reader.GetSampleInfo(0, 1).Duration.Value);
            Assert.Equal(emptyInterval ? 120 : 140, reader.Tracks[0].PacketIndex.DecodeDuration!.Value.Value);
        }

        private static byte[] Fragment(uint sequence, uint decodeTime, uint count, uint duration, bool empty)
        {
            byte[] moof = Mp4TestEncoding.Write(writer =>
            {
                int movie = writer.Begin("moof");
                int header = writer.Full("mfhd");
                writer.UInt32(sequence);
                writer.End(header);
                int track = writer.Begin("traf");
                int defaults = writer.Full("tfhd", empty ? 0x030038u : 0x020038u);
                writer.UInt32(1);
                writer.UInt32(duration);
                writer.UInt32(1);
                writer.UInt32(0);
                writer.End(defaults);
                int time = writer.Full("tfdt");
                writer.UInt32(decodeTime);
                writer.End(time);
                if (!empty)
                {
                    int run = writer.Full("trun", 1);
                    writer.UInt32(count);
                    writer.UInt32(0);
                    writer.End(run);
                }
                writer.End(track);
                writer.End(movie);
            });
            if (empty)
            {
                return moof;
            }
            Mp4TestData.SetUInt32(moof, "trun", 8, (uint)moof.Length + 8);
            byte[] media = Mp4TestEncoding.Write(writer =>
            {
                int data = writer.Begin("mdat");
                for (uint index = 0; index < count; index++) { writer.Byte((byte)index); }
                writer.End(data);
            });
            return [.. moof, .. media];
        }

        private static async Task VerifyFailureAsync<T>(byte[] data, Mp4ReadOptions? options = null, long memoryBudget = 1024 * 1024)
            where T : Exception
        {
            MediaBufferAllocator allocator = new(memoryBudget);
            await using TrackingRandomAccessSource source = new(data);
            await Assert.ThrowsAsync<T>(() => Mp4Reader.OpenAsync(source, allocator, options).AsTask());
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.IsDisposed);
        }
    }
}
