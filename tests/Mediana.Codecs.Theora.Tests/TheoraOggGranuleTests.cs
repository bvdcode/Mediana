// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Containers.Ogg;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Codecs.Theora.Tests
{
    public class TheoraOggGranuleTests
    {
        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 7)]
        public async Task VersionedGranulesRetainFrameClocksAndKeyframeOffsets(int revision, int origin)
        {
            byte[] data = await DataAsync(); Patch(data, revision, origin, false);
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using RandomAccessStreamByteSource source = new(new MemoryStream(data, false));
            await using (OggReader reader = await OggReader.OpenAsync(source, allocator))
            {
                Assert.Equal(24, reader.GetPacketCount(0));
                for (int i = 0; i < 24; i++)
                {
                    using MediaPacket packet = await reader.ReadPacketAtAsync(0, i);
                    Assert.Equal(i + origin, packet.PresentationTime!.Value.Value);
                    Assert.Equal(packet.PresentationTime, reader.GetPacketTiming(0, i).PresentationTime);
                    Assert.Equal(i / 12 * 12, reader.FindSeekPoint(0, packet.PresentationTime.Value, 24)!.Value.PacketIndex);
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task InconsistentFinalGranuleFailsWithoutRetainingIndexPages()
        {
            byte[] data = await DataAsync(); Patch(data, 1, 0, true);
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using RandomAccessStreamByteSource source = new(new MemoryStream(data, false));
            await Assert.ThrowsAsync<InvalidDataException>(async () => await OggReader.OpenAsync(source, allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static Task<byte[]> DataAsync() => File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "main420.ogv"));

        private static void Patch(byte[] data, int revision, int origin, bool corruptEnd)
        {
            int offset = 0, shift = 0;
            while (offset < data.Length)
            {
                int segments = data[offset + 26], body = offset + 27 + segments, length = 27 + segments;
                for (int i = 0; i < segments; i++) { length += data[offset + 27 + i]; }
                if ((data[offset + 5] & 2) != 0)
                { data[body + 9] = (byte)revision; shift = (BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(body + 40)) >> 5) & 31; }
                long granule = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(offset + 6));
                if (granule > 0)
                {
                    long adjustment = origin - (revision == 0 ? 1 : 0);
                    granule = checked(granule + (adjustment << shift));
                    if (corruptEnd && offset + length == data.Length) { granule++; }
                    BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(offset + 6), granule);
                }
                data.AsSpan(offset + 22, 4).Clear(); uint crc = 0;
                for (int i = 0; i < length; i++)
                {
                    crc ^= (uint)data[offset + i] << 24;
                    for (int b = 0; b < 8; b++) { crc = (crc & 0x80000000u) != 0 ? (crc << 1) ^ 0x04C11DB7u : crc << 1; }
                }
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset + 22), crc); offset += length;
            }
        }
    }
}
