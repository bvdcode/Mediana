// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Formats.IsoBmff;

namespace Mediana.Codecs.Raster.Tests
{
    public class IsoBmffBoxReaderTests
    {
        [Fact]
        public void ExtendedSizeAndUuidPreserveBorrowedPayloadAndAbsoluteOffset()
        {
            byte[] file = new byte[35];
            BinaryPrimitives.WriteUInt32BigEndian(file, 1);
            "uuid"u8.CopyTo(file.AsSpan(4)); BinaryPrimitives.WriteUInt64BigEndian(file.AsSpan(8), 35);
            for (int i = 0; i < 16; i++) { file[16 + i] = (byte)i; }
            file[32] = 10; file[33] = 20; file[34] = 30;
            IsoBmffBoxReader reader = new(file, 100);
            Assert.True(reader.TryRead(out IsoBmffBox box));
            Assert.Equal("uuid", box.TypeName); Assert.Equal(132, box.PayloadOffset);
            Assert.Equal(file.AsSpan(16, 16).ToArray(), box.ExtendedType.ToArray());
            Assert.Equal(new byte[] { 10, 20, 30 }, box.Payload.ToArray());
            file[32] = 9; Assert.Equal(9, box.Payload.Span[0]);
            Assert.False(reader.TryRead(out _));
        }

        [Fact]
        public void ZeroSizeConsumesOnlyTheParentAndTruncatedHeadersAreErrors()
        {
            byte[] file = [0, 0, 0, 0, 109, 100, 97, 116, 1, 2, 3];
            IsoBmffBoxReader reader = new(file);
            Assert.True(reader.TryRead(out IsoBmffBox box)); Assert.Equal(3, box.Payload.Length);
            Assert.False(reader.TryRead(out _));
            for (int length = 1; length < 8; length++)
            { Assert.Throws<InvalidDataException>(() => ReadOne(file.AsMemory(0, length))); }
            file[3] = 12; Assert.Throws<InvalidDataException>(() => ReadOne(file));
            file[3] = 1; Assert.Throws<InvalidDataException>(() => ReadOne(file));
            Assert.Throws<ArgumentException>(() => IsoBmffBox.Code("longer"));
        }

        private static void ReadOne(ReadOnlyMemory<byte> file)
        {
            IsoBmffBoxReader reader = new(file);
            reader.TryRead(out _);
        }
    }
}
