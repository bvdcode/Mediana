// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Formats.Audio;
using Mediana.Tests;

namespace Mediana.Codecs.Vorbis.Tests
{
    public class VorbisCodebookTests
    {
        [Fact]
        public void ScalarAndVectorBooksMatchIndependentReference() =>
            Assert.Equal(49152, VorbisCodebookTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data")));

        [Theory]
        [InlineData(new byte[] { 1, 1, 1 })]
        [InlineData(new byte[] { 2, 2 })]
        [InlineData(new byte[] { 33 })]
        public void InvalidTreesReleaseAllocatedStorage(byte[] lengths)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            Assert.Throws<InvalidDataException>(() => new VorbisCodebook(1, lengths, 0, 0, 0, false, [], allocator, default));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void LookupAndEntropyDecodingDoNotAllocateWorkingStorage()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using VorbisCodebook book = new(2, new byte[] { 1, 2, 2 }, 1, 0, 0x60100000, true, new ushort[] { 1 }, allocator, default);
            float[] output = new float[2]; byte[] data = new byte[64];
            for (int pass = 0; pass < 20; pass++) { Decode(book, data, output); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int pass = 0; pass < 100; pass++) { Decode(book, data, output); }
            Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        }

        private static void Decode(VorbisCodebook book, byte[] data, float[] output)
        {
            VorbisBitReader reader = new(data);
            for (int index = 0; index < 512; index++)
            {
                if (!book.TryDecode(ref reader, out int entry)) { throw new InvalidDataException("A complete codeword was not read."); }
                book.ReadVector(entry, output);
            }
        }

        [Fact]
        public void BitReaderHandlesUnalignedThirtyTwoBitValuesAndEmptyReads()
        {
            byte[] bytes = [0xf3, 0x78, 0x56, 0x34, 0x12, 0xff];
            VorbisBitReader reader = new(bytes);
            Assert.Equal(0u, reader.Read(0));
            Assert.Equal(3u, reader.Read(4));
            Assert.True(reader.TryPeek(32, out uint peek));
            Assert.Equal(0x2345678fu, peek);
            Assert.Equal(peek, reader.Read(32));
            Assert.Equal(12, reader.Remaining);
            Assert.False(reader.TryPeek(13, out _));
            Assert.Equal(0xff1u, reader.Read(12));
        }
    }
}
