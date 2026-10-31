// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Globalization;
using Mediana.Containers.Mp4;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class Mp4HeaderDateTests
    {
        [Fact]
        public async Task RealHeaderDatesMatchIndependentRawCalendarAndCompletePacketReferences() =>
            await Mp4HeaderDateTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Metadata"));

        [Theory]
        [InlineData("0", null)]
        [InlineData("1", "1904-01-01T00:00:01.0000000Z")]
        [InlineData("2082844800", "1970-01-01T00:00:00.0000000Z")]
        [InlineData("4294967295", "2040-02-06T06:28:15.0000000Z")]
        [InlineData("4294967296", "2040-02-06T06:28:16.0000000Z")]
        [InlineData("18446744073709551615", null)]
        public void RawDatesRemainUnsignedAndCalendarConversionDoesNotWrapOrUseUnixHeuristics(string raw, string? expected)
        {
            ulong seconds = ulong.Parse(raw, CultureInfo.InvariantCulture);
            Mp4HeaderDates dates = new(seconds, seconds, DateTimeKind.Utc);
            Assert.Equal(seconds, dates.CreationSeconds);
            Assert.Equal(seconds, dates.ModificationSeconds);
            Assert.Equal(expected, dates.CreationTime?.ToString("O", CultureInfo.InvariantCulture));
            Assert.Equal(expected, dates.ModificationTime?.ToString("O", CultureInfo.InvariantCulture));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Mp4HeaderDates(1, 1, DateTimeKind.Local));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Mp4HeaderDates(1, 1, (DateTimeKind)99));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task BothHeaderVersionsPreservePre1970ValuesAndSeparateMovieTrackAndMediaDates(bool versionOne)
        {
            byte[] file = await Mp4TestData.ReadAsync("avc-tail.mov");
            if (versionOne)
            {
                file = Mp4TestData.Use64BitTimeHeaders(file);
            }
            int value = 1;
            foreach (string type in new[] { "mvhd", "tkhd", "mdhd" })
            {
                int offset = Mp4TestData.PayloadOffset(file, type);
                if (versionOne)
                {
                    BinaryPrimitives.WriteUInt64BigEndian(file.AsSpan(offset + 4), (ulong)value);
                    BinaryPrimitives.WriteUInt64BigEndian(file.AsSpan(offset + 12), (ulong)(value + 1));
                }
                else
                {
                    BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(offset + 4), (uint)value);
                    BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(offset + 8), (uint)(value + 1));
                }
                value += 2;
            }
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(file, 7);
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator))
            {
                Assert.Equal(1UL, reader.HeaderDates.CreationSeconds);
                Assert.Equal(3UL, reader.Tracks[0].HeaderDates.CreationSeconds);
                Assert.Equal(5UL, reader.Tracks[0].MediaDates.CreationSeconds);
                Assert.Equal("1904-01-01T00:00:01.0000000", reader.HeaderDates.CreationTime!.Value.ToString("O", CultureInfo.InvariantCulture));
                Assert.Equal(DateTimeKind.Unspecified, reader.HeaderDates.CreationTime.Value.Kind);
                Assert.Equal(new[] { "mvhd", "mvhd" }, reader.Info.Dates!.Select(date => date.Namespace));
                Assert.Equal(new[] { "tkhd", "tkhd", "mdhd", "mdhd" }, reader.Info.StreamInfo![0].Dates!.Select(date => date.Namespace));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData("qt  ", "qt  ", false, DateTimeKind.Unspecified)]
        [InlineData("isom", "qt  ", false, DateTimeKind.Unspecified)]
        [InlineData("isom", "isom", false, DateTimeKind.Utc)]
        [InlineData("qt  ", "qt  ", true, DateTimeKind.Unspecified)]
        public async Task IsoUtcAndQuickTimeOrUnbrandedUnknownZonesFollowTheOriginalBrandContext(string major, string compatible, bool unbranded, DateTimeKind expected)
        {
            byte[] file = await Mp4TestData.ReadAsync("avc-tail.mov");
            int payload = Mp4TestData.PayloadOffset(file, "ftyp");
            System.Text.Encoding.ASCII.GetBytes(major).CopyTo(file.AsSpan(payload, 4));
            System.Text.Encoding.ASCII.GetBytes(compatible).CopyTo(file.AsSpan(payload + 8, 4));
            if (unbranded)
            {
                "free"u8.CopyTo(file.AsSpan(payload - 4, 4));
            }
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(file);
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator))
            {
                Assert.Equal(expected, reader.HeaderDates.Kind);
                Assert.All(reader.Tracks, track =>
                {
                    Assert.Equal(expected, track.HeaderDates.Kind);
                    Assert.Equal(expected, track.MediaDates.Kind);
                });
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
