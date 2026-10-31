// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text;
using Mediana.Containers.Mp4;
using Mediana.Containers;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class Mp4TrackMetadataTests
    {
        [Fact]
        public async Task ThreeRealTracksRetainNativeScopesLanguagesBytesAndEveryPacket() =>
            await Mp4TrackMetadataTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Metadata"));

        [Theory]
        [InlineData(0, null, "eng")]
        [InlineData(32, null, "rus")]
        [InlineData(19, null, "zh-Hant")]
        [InlineData(33, null, "zh-Hans")]
        [InlineData(138, null, "jv-Latn")]
        [InlineData(86, null, null)]
        [InlineData(0x7fff, null, null)]
        [InlineData(0x400, null, null)]
        [InlineData(0x8000, null, null)]
        [InlineData(0x4ab3, null, "rus")]
        [InlineData(0x55c4, null, "und")]
        [InlineData(0, "en-AU", "en-AU")]
        [InlineData(32, "EN-us", "EN-us")]
        [InlineData(0, "i-klingon", "i-klingon")]
        [InlineData(0, "x-catalog", "x-catalog")]
        [InlineData(0, "", null)]
        [InlineData(0, "en_US", null)]
        [InlineData(0, "en-", null)]
        [InlineData(0, "en-ü", null)]
        public async Task OriginalLanguageCodesRemainAvailableAndInvalidExtendedTagsMeanUnknown(int code, string? tag, string? expected)
        {
            byte[] data = await Mp4TestData.ReadAsync("avc-tail.mov");
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(Mp4TestData.PayloadOffset(data, "mdhd") + 20), (ushort)code);
            if (tag is not null)
            {
                data = Mp4MetadataTestData.AppendTo(data, Mp4MetadataTestData.Box("elng", new byte[4], [.. Encoding.UTF8.GetBytes(tag), 0]), "moov", "trak", "mdia");
            }
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(data, 7);
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator))
            {
                Assert.Equal((ushort)code, reader.Tracks[0].Language.Code);
                Assert.Equal(expected, reader.Tracks[0].Language.Language);
                Assert.Equal(expected, reader.Info.StreamInfo![0].Language);
                string? original = null;
                if (tag is not null)
                {
                    original = Encoding.Latin1.GetString(Encoding.UTF8.GetBytes(tag));
                }
                Assert.Equal(original, reader.Tracks[0].Language.ExtendedTag);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task VersionOneHeadersPreserveLanguageCodesAndExtendedTagSelection(bool extended)
        {
            byte[] data = await Mp4TestData.ReadAsync("avc-tail.mov");
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(Mp4TestData.PayloadOffset(data, "mdhd") + 20), 0);
            data = Mp4TestData.Use64BitTimeHeaders(data);
            if (extended)
            {
                data = Mp4MetadataTestData.AppendTo(data, Mp4MetadataTestData.Box("elng", new byte[4], "fr-CA\0"u8.ToArray()), "moov", "trak", "mdia");
            }
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(data, 7);
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator))
            {
                Assert.Equal(0, reader.Tracks[0].Language.Code);
                if (extended)
                {
                    Assert.Equal("fr-CA", reader.Info.StreamInfo![0].Language);
                }
                else
                {
                    Assert.Equal("eng", reader.Info.StreamInfo![0].Language);
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task UnbrandedAndIsoFilesDoNotInventMacintoshEnglish(bool unbranded)
        {
            byte[] data = await Mp4TestData.ReadAsync("avc-tail.mov");
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(Mp4TestData.PayloadOffset(data, "mdhd") + 20), 0);
            int payload = Mp4TestData.PayloadOffset(data, "ftyp");
            if (unbranded)
            {
                "free"u8.CopyTo(data.AsSpan(payload - 4, 4));
            }
            else
            {
                "isom"u8.CopyTo(data.AsSpan(payload, 4));
                "isom"u8.CopyTo(data.AsSpan(payload + 8, 4));
            }
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(data);
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator))
            {
                Assert.Null(reader.Info.StreamInfo![0].Language);
                Assert.Equal(0, reader.Tracks[0].Language.Code);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(16, 326)]
        [InlineData(17, 325)]
        public async Task MetadataLimitsApplyAcrossAllTracksMediaAndMovie(int values, int textBytes)
        {
            byte[] data = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Metadata", "track-metadata.mp4"));
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(data, 7);
            Mp4ReadOptions options = new() { MaxMetadataValues = values, MaxMetadataTextBytes = textBytes };
            await Assert.ThrowsAsync<NotSupportedException>(async () => await Mp4Reader.OpenAsync(source, allocator, options));
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.IsDisposed);
        }

        [Fact]
        public async Task CompatibleQuickTimeBrandSelectsMacintoshCodesWithADifferentMajorBrand()
        {
            byte[] data = await Mp4TestData.ReadAsync("avc-tail.mov");
            "xcmp"u8.CopyTo(data.AsSpan(Mp4TestData.PayloadOffset(data, "ftyp"), 4));
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(Mp4TestData.PayloadOffset(data, "mdhd") + 20), 0);
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(data);
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator))
            {
                Assert.Equal("xcmp", reader.MajorBrand);
                Assert.Equal("eng", reader.Info.StreamInfo![0].Language);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task MetadataNeverReadsEncodedPayloadsAndLateSnapshotsSurviveDisposal()
        {
            byte[] data = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Metadata", "track-metadata.mp4"));
            int first = Mp4TestData.PayloadOffset(data, "mdat");
            int end = first - 8 + (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(first - 8));
            data.AsSpan(first, end - first).Clear();
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(data, 7);
            Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadTrackMetadataValue(-1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadTrackMetadataValue(3, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadTrackMetadataValue(0, 4));
            await reader.DisposeAsync();
            Assert.Equal(0, allocator.AllocatedBytes);
            ContainerInfo[] snapshots = new ContainerInfo[32];
            Parallel.For(0, snapshots.Length, index => snapshots[index] = reader.Info);
            Assert.All(snapshots, info => Assert.Same(snapshots[0], info));
            Assert.Equal("ru-RU", snapshots[0].StreamInfo![1].Language);
            Assert.Equal(3, snapshots[0].StreamInfo![1].Tags!.Count);
            Assert.All(source.Reads, read => Assert.True(read.Offset + read.Length <= first || read.Offset >= end));
            Assert.Throws<ObjectDisposedException>(() => reader.ReadTrackMetadataValue(0, 0));
        }

        [Fact]
        public async Task CancellationWhileReadingFileTypeReleasesBuffersWithoutReadingMovieOrPackets()
        {
            byte[] data = await Mp4TestData.ReadAsync("avc-tail.mov");
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(data, 4);
            using CancellationTokenSource cancellation = new();
            source.AfterRead = cancellation.Cancel;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await Mp4Reader.OpenAsync(source, allocator, cancellationToken: cancellation.Token));
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.IsDisposed);
        }

        [Fact]
        public async Task TruncatedAndDuplicatedExtendedLanguageBoxesFailWithoutRetainingStorage()
        {
            byte[][] invalid = [[], new byte[4], [0, 0, 0, 0, (byte)'e', (byte)'n'], [0, 0, 0, 0, 0, (byte)'x', 0]];
            foreach (byte[] payload in invalid)
            {
                await VerifyInvalidAsync(Mp4MetadataTestData.Box("elng", payload));
            }
            byte[] good = Mp4MetadataTestData.Box("elng", new byte[4], "en-US\0"u8.ToArray());
            await VerifyInvalidAsync([.. good, .. good]);
        }

        private static async Task VerifyInvalidAsync(byte[] addition)
        {
            byte[] data = Mp4MetadataTestData.AppendTo(await Mp4TestData.ReadAsync("avc-tail.mov"), addition, "moov", "trak", "mdia");
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(data);
            await Assert.ThrowsAsync<InvalidDataException>(async () => await Mp4Reader.OpenAsync(source, allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
