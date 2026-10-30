// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class H264EncodingSyntaxTests
    {
        [Theory]
        [InlineData(-1, 4)]
        [InlineData(0, 16)]
        [InlineData(1, 15)]
        [InlineData(2, 16)]
        [InlineData(4, 16)]
        [InlineData(8, 16)]
        public void CavlcSparseDenseAndEscapeLevelsRoundTrip(int neighborCount, int size)
        {
            Random random = new(342 + neighborCount);
            MediaBufferAllocator allocator = new(65536);
            for (int iteration = 0; iteration < 300; iteration++)
            {
                int[] levels = new int[size];
                for (int index = 0; index < size; index++)
                {
                    int magnitude = (iteration % 3) switch { 0 => 1, 1 => 40, _ => 2047 };
                    levels[index] = random.Next(3) == 0 ? 0 : random.Next(-magnitude, magnitude + 1);
                }
                using H264BitWriter bits = new(allocator, 4096, 0x65);
                int count = H264CavlcWriter.Write(bits, levels, neighborCount);
                Assert.Equal(levels.Count(level => level != 0), count);
                using MediaBuffer nal = bits.Finish();
                H264BitReader reader = H264Nal.Payload(nal.Memory.Span, 5);
                int[] actual = new int[size];
                Assert.Equal(count, H264Cavlc.Read(ref reader, actual, neighborCount));
                Assert.Equal(levels, actual);
                reader.ReadTrailingBits();
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void ExpGolombAndEmulationPreventionPreserveRbsp()
        {
            MediaBufferAllocator allocator = new(16384);
            using H264BitWriter writer = new(allocator, 8192, 0x67);
            uint[] values = [0, 1, 2, 255, 65535, int.MaxValue];
            foreach (uint value in values)
            {
                writer.WriteUnsigned(value);
            }
            writer.WriteBits(0, 32);
            writer.WriteBits(1, 24);
            writer.WriteSigned(-777);
            using MediaBuffer encoded = writer.Finish();
            H264BitReader reader = H264Nal.Payload(encoded.Memory.Span, 7);
            foreach (uint value in values)
            {
                Assert.Equal(value, (uint)reader.ReadUe());
            }
            Assert.Equal(0U, reader.ReadBits(32));
            Assert.Equal(1U, reader.ReadBits(24));
            Assert.Equal(-777, reader.ReadSe());
            reader.ReadTrailingBits();
        }
    }
}
