// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text;
using Mediana.Containers.Mp4;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class Mp4MetadataTests
    {
        [Fact]
        public Task RealItunesAndIndexedMetadataAndOriginalCoversMatchIndependentReferences() =>
            Mp4MetadataTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Metadata"));

        [Fact]
        public async Task RepeatedLocalizedTextFreeformBinaryAndEmptyValuesRemainDistinct()
        {
            byte[] utf8 = Encoding.UTF8.GetBytes("Signal 🌊");
            byte[] utf16 = Encoding.BigEndianUnicode.GetBytes("Сигнал 🧪");
            byte[] name = Mp4MetadataTestData.Box("©nam", Mp4MetadataTestData.Data(1, 0, utf8), Mp4MetadataTestData.Data(2, 0x474215c7, utf16));
            byte[] freeform = Mp4MetadataTestData.Box("----", Mp4MetadataTestData.Box("mean", new byte[4], "example.media"u8.ToArray()),
                Mp4MetadataTestData.Box("name", new byte[4], "catalog-id"u8.ToArray()), Mp4MetadataTestData.Data(1, 0, "test-17"u8.ToArray()));
            byte[] binary = Mp4MetadataTestData.Box("covr", Mp4MetadataTestData.Data(0x01000001, 0, [0xff, 0, 1]));
            byte[] empty = Mp4MetadataTestData.Box("©cmt", Mp4MetadataTestData.Data(1, 0, []));
            byte[] meta = Mp4MetadataTestData.Meta("mdir", [.. name, .. freeform, .. binary, .. empty]);
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4MetadataTestData.FileAsync(Mp4MetadataTestData.Box("udta", meta)), 1);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            Assert.Equal(5, reader.Metadata.Count);
            Assert.Equal("Signal 🌊", reader.Metadata[0].Text);
            Assert.Equal("Сигнал 🧪", reader.Metadata[1].Text);
            Assert.Equal(0x474215c7u, reader.Metadata[1].Locale);
            Assert.Equal("example.media", reader.Metadata[2].Namespace);
            Assert.Equal("catalog-id", reader.Metadata[2].Name);
            Assert.Null(reader.Metadata[3].Text);
            Assert.Equal(string.Empty, reader.Metadata[4].Text);
            Assert.Single(reader.CoverArt);
            Assert.Null(reader.CoverArt[0].ContentType);
            using MediaBuffer raw = reader.ReadCoverArt(0);
            Assert.Equal(new byte[] { 0xff, 0, 1 }, raw.Memory.ToArray());
            using MediaBuffer zero = reader.ReadMetadataValue(4);
            Assert.Empty(zero.Memory.ToArray());
            Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadMetadataValue(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadCoverArt(1));
            await reader.DisposeAsync();
            Assert.Throws<ObjectDisposedException>(() => reader.ReadMetadataValue(0));
            Assert.False(source.IsDisposed);
        }

        [Fact]
        public async Task IndexedArtworkUsesItsNamespaceAndPreservesTypedBytes()
        {
            byte[] key = Mp4MetadataTestData.Box("mdta", "com.apple.quicktime.artwork"u8.ToArray());
            byte[] header = new byte[8];
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), 1);
            byte[] keys = Mp4MetadataTestData.Box("keys", header, key);
            byte[] data = [0x89, .. "PNG\r\n\x1a\n"u8, 1, 2, 3];
            byte[] list = Mp4MetadataTestData.Box("\0\0\0\u0001", Mp4MetadataTestData.Data(14, 17, data));
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4MetadataTestData.FileAsync(Mp4MetadataTestData.Meta("mdta", list, keys)));
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            Assert.Single(reader.Metadata);
            Assert.Single(reader.CoverArt);
            Assert.Equal("image/png", reader.CoverArt[0].ContentType);
            Assert.Equal(17u, reader.CoverArt[0].Locale);
            using MediaBuffer image = reader.ReadCoverArt(0);
            Assert.Equal(data, image.Memory.ToArray());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task MetadataValueAndTextLimitsReleaseAllOpenStorage(bool textLimit)
        {
            byte[] entry = Mp4MetadataTestData.Box("©nam", Mp4MetadataTestData.Data(1, 0, "value"u8.ToArray()));
            byte[] file = await Mp4MetadataTestData.FileAsync(Mp4MetadataTestData.Meta("mdir", [.. entry, .. entry]));
            Mp4ReadOptions options = new() { MaxMetadataValues = 1 };
            if (textLimit)
            {
                options = new Mp4ReadOptions { MaxMetadataTextBytes = 1 };
            }
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(file);
            await Assert.ThrowsAsync<NotSupportedException>(async () => await Mp4Reader.OpenAsync(source, allocator, options));
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.IsDisposed);
        }

        [Fact]
        public async Task MalformedDataKeysAndDuplicatedBoxesAreRejectedWithoutLeakingBuffers()
        {
            byte[] good = Mp4MetadataTestData.Box("©nam", Mp4MetadataTestData.Data(1, 0, "title"u8.ToArray()));
            byte[] indexed = Mp4MetadataTestData.Box("\0\0\0\0", Mp4MetadataTestData.Data(1, 0, "text"u8.ToArray()));
            byte[] keys = Mp4MetadataTestData.Box("keys", new byte[8]);
            byte[] meta = Mp4MetadataTestData.Meta("mdir", good);
            byte[][] invalid = [Mp4MetadataTestData.Meta("mdir", Mp4MetadataTestData.Box("covr", Mp4MetadataTestData.Box("data", new byte[7]))),
                Mp4MetadataTestData.Meta("mdta", indexed, keys), [.. meta, .. meta],
                Mp4MetadataTestData.Meta("mdir", Mp4MetadataTestData.Box("----", Mp4MetadataTestData.Data(1, 0, [])))];
            foreach (byte[] malformed in invalid)
            {
                MediaBufferAllocator allocator = new(1024 * 1024);
                await using TrackingRandomAccessSource source = new(await Mp4MetadataTestData.FileAsync(malformed));
                await Assert.ThrowsAsync<InvalidDataException>(async () => await Mp4Reader.OpenAsync(source, allocator));
                Assert.Equal(0, allocator.AllocatedBytes);
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public async Task InvalidUnicodeAndUnsupportedHandlersDoNotInventText(uint type)
        {
            byte[] invalid = type switch
            {
                1 => [0xc3],
                2 => [0xd8, 0],
                _ => throw new ArgumentOutOfRangeException(nameof(type))
            };
            byte[] badText = Mp4MetadataTestData.Box("©nam", Mp4MetadataTestData.Data(type, 0, invalid));
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4MetadataTestData.FileAsync(Mp4MetadataTestData.Meta("mdir", badText)));
            await Assert.ThrowsAsync<DecoderFallbackException>(async () => await Mp4Reader.OpenAsync(source, allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
            await using TrackingRandomAccessSource unknown = new(await Mp4MetadataTestData.FileAsync(Mp4MetadataTestData.Meta("test", [])));
            await Assert.ThrowsAsync<NotSupportedException>(async () => await Mp4Reader.OpenAsync(unknown, allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
