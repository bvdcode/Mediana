// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;

namespace Mediana.Tests
{
    public class Mp4UserDataTests
    {
        [Fact]
        public async Task ScopedNamesRetainStructuralOrderLanguageOriginalPayloadAndStorageLifetime()
        {
            byte[] localized = LocalizedPayload(0x4ab3, [0xfe, 0xff, .. Encoding.BigEndianUnicode.GetBytes("Название 🎵"), 0, 0]);
            byte[] typed = Mp4MetadataTestData.Meta("mdir", Mp4MetadataTestData.Box("©nam", Mp4MetadataTestData.Data(1, 7, "typed"u8.ToArray())));
            byte[] file = await Mp4MetadataTestData.FileAsync(Mp4MetadataTestData.Box("udta", Mp4MetadataTestData.Box("name", "movie"u8.ToArray())));
            file = Mp4MetadataTestData.AppendTo(file, Mp4MetadataTestData.Box("udta",
                Mp4MetadataTestData.Box("name", "first"u8.ToArray()), typed, Mp4MetadataTestData.Box("tnam", localized),
                Mp4MetadataTestData.Box("name", [])), "moov", "trak");
            file = Mp4MetadataTestData.AppendTo(file, Mp4MetadataTestData.Box("udta", Mp4MetadataTestData.Box("name", "media"u8.ToArray())), "moov", "trak", "mdia");
            MediaBufferAllocator allocator = new(1024 * 1024);
            MediaBuffer? retained = null;
            await using TrackingRandomAccessSource source = new(file, 5);
            try
            {
                await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
                Assert.Equal("movie", Assert.Single(reader.UserData).Text);
                Assert.Equal("movie", Assert.Single(Assert.Single(reader.Info.Tags!).Values));
                Mp4TrackInfo track = reader.Tracks[0];
                IReadOnlyList<MediaTag> tags = reader.Info.StreamInfo![0].Tags!;
                Assert.Equal(new[] { "media", "first", "typed", "Название 🎵", "" }, tags.Select(tag => Assert.Single(tag.Values)));
                Assert.Equal(new[] { "udta", "udta", "mdir", "udta", "udta" }, tags.Select(tag => tag.Namespace));
                Assert.Equal(new[] { "trak/mdia/udta", "trak/udta", "trak/udta/meta", "trak/udta", "trak/udta" }, tags.Select(tag => tag.SourcePath));
                Assert.Equal("rus", tags[3].Language);
                Assert.Null(tags[3].Locale);
                Assert.Equal(7u, tags[2].Locale);
                Assert.Equal((ushort)0x4ab3, track.UserData[2].LanguageCode);
                IList<Mp4UserDataItem> items = Assert.IsAssignableFrom<IList<Mp4UserDataItem>>(track.UserData);
                Assert.Throws<NotSupportedException>(() => items.Clear());
                Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadTrackUserDataValue(-1, 0));
                Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadTrackUserDataValue(reader.Tracks.Count, 0));
                Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadTrackUserDataValue(0, -1));
                Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadTrackUserDataValue(0, track.UserData.Count));
                Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadUserDataValue(1));
                retained = reader.ReadTrackUserDataValue(0, 2);
                Assert.Equal(localized, retained.Memory.ToArray());
                using MediaBuffer empty = reader.ReadTrackUserDataValue(0, 3);
                Assert.Empty(empty.Memory.ToArray());
                using MediaPacket? first = await reader.ReadPacketAsync();
                Assert.NotNull(first);
                Assert.Equal(0, reader.GetSampleInfo(0, 0).SampleIndex);
                await reader.DisposeAsync();
                Assert.Equal(localized, retained.Memory.ToArray());
                Assert.True(allocator.AllocatedBytes > 0);
                Assert.Throws<ObjectDisposedException>(() => reader.ReadTrackUserDataValue(0, 0));
                Assert.Equal("Название 🎵", reader.Info.StreamInfo[0].Tags![3].Values[0]);
            }
            finally
            {
                retained?.Dispose();
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.IsDisposed);
        }

        [Theory]
        [InlineData(0x15c7, "eng")]
        [InlineData(0x4ab3, "rus")]
        [InlineData(0x55c4, "und")]
        [InlineData(0, null)]
        [InlineData(0x7fff, null)]
        [InlineData(0xffff, null)]
        public async Task LocalizedTextLanguageIsPackedIsoAndNeverInheritsTheTrackLanguage(int code, string? language)
        {
            byte[] file = await FileAsync(Mp4MetadataTestData.Box("tnam", LocalizedPayload((ushort)code, "label\0"u8.ToArray())));
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(file);
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator))
            {
                Mp4UserDataItem item = Assert.Single(reader.Tracks[0].UserData);
                Assert.Equal((ushort)code, item.LanguageCode);
                Assert.Equal(language, item.Language);
                Assert.Equal(language, Assert.Single(reader.Info.StreamInfo![0].Tags!).Language);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData("")]
        [InlineData("00000000")]
        [InlineData("0000000055c4")]
        [InlineData("0000000055c478")]
        [InlineData("0000000155c47800")]
        [InlineData("0000000055c478007900")]
        [InlineData("0000000055c4feff")]
        [InlineData("0000000055c4feff000000")]
        [InlineData("0000000055c4feff0078000000790000")]
        public async Task TruncatedInvalidReservedAndEmbeddedTerminatedNamesFailWithoutLeaking(string hex)
        {
            byte[] file = await FileAsync(Mp4MetadataTestData.Box("tnam", Convert.FromHexString(hex)));
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(file, 3);
            await Assert.ThrowsAsync<InvalidDataException>(async () => await Mp4Reader.OpenAsync(source, allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.IsDisposed);
        }

        [Theory]
        [InlineData("name", "ff")]
        [InlineData("tnam", "0000000055c4c000")]
        [InlineData("tnam", "0000000055c4fffe7800")]
        [InlineData("tnam", "0000000055c4feffd8000000")]
        public async Task InvalidDeclaredUnicodeIsRejectedWithoutTryingAnotherEncoding(string type, string hex)
        {
            byte[] file = await FileAsync(Mp4MetadataTestData.Box(type, Convert.FromHexString(hex)));
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(file);
            await Assert.ThrowsAsync<DecoderFallbackException>(async () => await Mp4Reader.OpenAsync(source, allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task EmptyLocalizedNamesRemainEmptyAndUnknownUserDataIsNotInterpreted()
        {
            byte[] file = await FileAsync([.. Mp4MetadataTestData.Box("tnam", LocalizedPayload(0x15c7, [0])),
                .. Mp4MetadataTestData.Box("tnam", LocalizedPayload(0x4ab3, [0xfe, 0xff, 0, 0])),
                .. Mp4MetadataTestData.Box("zzzz", [0xff, 0xfe, 0xff])]);
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(file);
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, new() { MaxMetadataValues = 2, MaxMetadataTextBytes = 1 }))
            {
                Assert.Equal(new[] { "", "" }, reader.Tracks[0].UserData.Select(item => item.Text));
                Assert.Equal(new[] { "eng", "rus" }, reader.Info.StreamInfo![0].Tags!.Select(tag => tag.Language));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(2, 100)]
        [InlineData(3, 8)]
        public async Task TypedValuesAndUserDataShareMovieTrackAndMediaLimits(int values, int bytes)
        {
            byte[] file = await Mp4MetadataTestData.FileAsync(Mp4MetadataTestData.Box("udta", Mp4MetadataTestData.Box("name", "one"u8.ToArray())));
            file = Mp4MetadataTestData.AppendTo(file, Mp4MetadataTestData.Box("udta", Mp4MetadataTestData.Box("name", "two"u8.ToArray())), "moov", "trak");
            file = Mp4MetadataTestData.AppendTo(file, Mp4MetadataTestData.Meta("mdir", Mp4MetadataTestData.Box("©nam", Mp4MetadataTestData.Data(1, 0, "end"u8.ToArray()))), "moov", "trak", "mdia");
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(file);
            await Assert.ThrowsAsync<NotSupportedException>(async () => await Mp4Reader.OpenAsync(source, allocator,
                new() { MaxMetadataValues = values, MaxMetadataTextBytes = bytes }));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static byte[] LocalizedPayload(ushort code, byte[] text)
        {
            byte[] header = new byte[6];
            BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4), code);
            return [.. header, .. text];
        }

        private static async Task<byte[]> FileAsync(byte[] userData) => Mp4MetadataTestData.AppendTo(
            await Mp4MetadataTestData.FileAsync(Mp4MetadataTestData.Box("free", [])),
            Mp4MetadataTestData.Box("udta", userData), "moov", "trak");
    }
}
