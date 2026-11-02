// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Containers;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class Mp4PrerollTests
    {
        [Theory]
        [InlineData(-1, 0, false, 9)]
        [InlineData(-1, 3, false, 7)]
        [InlineData(-4, 1, false, 6)]
        [InlineData(-2, 1, true, 7)]
        [InlineData(short.MinValue, 1, false, 0)]
        [InlineData(-1, int.MaxValue, false, 0)]
        public async Task RecoveryAndCodecHistoryCombineWithoutReadingPayload(short distance, int minimum, bool sync, int expected)
        {
            byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Aac", "lc-mono-48.m4a"));
            int description = Mp4TestData.PayloadOffset(bytes, "sgpd");
            BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(description + 16), distance);
            if (sync)
            {
                "prol"u8.CopyTo(bytes.AsSpan(description + 4));
                "prol"u8.CopyTo(bytes.AsSpan(Mp4TestData.PayloadOffset(bytes, "sbgp") + 4));
                bytes = WithSyncSamples(bytes, [1, 4, 8, 12, 16]);
            }
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(bytes);
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator))
            {
                source.Reads.Clear();
                ContainerSeekPoint start = reader.GetPrerollStart(0, 10, minimum);
                Assert.Equal(expected, start.PacketIndex);
                Assert.Equal(reader.GetSampleInfo(0, expected).PresentationTime, start.PresentationTime);
                Assert.Empty(source.Reads);
                using MediaPacket packet = (await reader.ReadPacketAsync())!;
                Assert.Equal(reader.GetSampleInfo(0, 0).PresentationTime, packet.PresentationTime);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task ForwardRecoveryMissingSyncInvalidIndicesAndCancellationFailExplicitly()
        {
            byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Aac", "lc-mono-48.m4a"));
            int description = Mp4TestData.PayloadOffset(bytes, "sgpd");
            BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(description + 16), 2);
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource forwardSource = new(bytes.ToArray());
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(forwardSource, allocator))
            {
                Assert.Throws<NotSupportedException>(() => reader.GetPrerollStart(0, 10, 1));
                Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetPrerollStart(0, -1, 1));
                Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetPrerollStart(0, reader.Tracks[0].SampleCount, 1));
                Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetPrerollStart(0, 0, -1));
                Assert.Throws<OperationCanceledException>(() => reader.GetPrerollStart(0, 0, 1, new(true)));
            }
            BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(description + 16), -2);
            "prol"u8.CopyTo(bytes.AsSpan(description + 4));
            "prol"u8.CopyTo(bytes.AsSpan(Mp4TestData.PayloadOffset(bytes, "sbgp") + 4));
            bytes = WithSyncSamples(bytes, [12, 16]);
            await using TrackingRandomAccessSource missingSource = new(bytes);
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(missingSource, allocator))
            {
                Assert.Throws<NotSupportedException>(() => reader.GetPrerollStart(0, 10, 1));
                Assert.Equal(0, reader.GetPrerollStart(0, 0, 1).PacketIndex);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static byte[] WithSyncSamples(byte[] bytes, uint[] numbers)
        {
            Assert.True(Mp4TestData.PayloadOffset(bytes, "moov") > Mp4TestData.PayloadOffset(bytes, "mdat"));
            byte[] sync = new byte[16 + numbers.Length * 4];
            BinaryPrimitives.WriteUInt32BigEndian(sync, (uint)sync.Length);
            "stss"u8.CopyTo(sync.AsSpan(4));
            BinaryPrimitives.WriteUInt32BigEndian(sync.AsSpan(12), (uint)numbers.Length);
            for (int index = 0; index < numbers.Length; index++)
            {
                BinaryPrimitives.WriteUInt32BigEndian(sync.AsSpan(16 + index * 4), numbers[index]);
            }
            int table = Mp4TestData.PayloadOffset(bytes, "stbl") - 8;
            int end = checked(table + (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(table)));
            foreach (string name in new[] { "moov", "trak", "mdia", "minf", "stbl" })
            {
                int box = Mp4TestData.PayloadOffset(bytes, name) - 8;
                uint size = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(box));
                BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(box), checked(size + (uint)sync.Length));
            }
            return [.. bytes.AsSpan(0, end), .. sync, .. bytes.AsSpan(end)];
        }
    }
}
