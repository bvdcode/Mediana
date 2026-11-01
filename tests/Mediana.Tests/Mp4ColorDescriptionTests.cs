// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class Mp4ColorDescriptionTests
    {
        [Theory]
        [InlineData("container-709-limited.mp4", 1, 1, 1, false)]
        [InlineData("container-709-full.mp4", 1, 1, 1, true)]
        [InlineData("container-709.mov", 1, 1, 1, null)]
        [InlineData("container-601-conflict.mp4", 6, 1, 6, true)]
        [InlineData("container-2020-pq.mp4", 9, 16, 9, false)]
        public async Task RealNclxAndNclcDeclarationsMatchAuthoredContainerFields(string file, int primaries, int transfer, int matrix, bool? range)
        {
            MediaBufferAllocator allocator = new(128 * 1024);
            await using TrackingRandomAccessSource source = new(await File.ReadAllBytesAsync(Mp4ColorTestData.PathFor(file)), 7);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            Mp4TrackInfo track = Assert.Single(reader.Tracks);
            CicpColorDescription color = Assert.IsType<CicpColorDescription>(Assert.Single(track.ColorDescriptions));
            Assert.Equal(primaries, color.Primaries);
            Assert.Equal(transfer, color.Transfer);
            Assert.Equal(matrix, color.Matrix);
            Assert.Equal(range, color.FullRange);
            Assert.Same(color, track.Stream.ColorDescription);
            Assert.Same(color, Assert.Single(reader.Streams).ColorDescription);
            using MediaPacket? packet = await reader.ReadPacketAsync();
            Assert.NotNull(packet);
            await reader.DisposeAsync();
            packet.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.IsDisposed);
        }

        [Fact]
        public async Task OrderedAlternativesRetainIccBytesAndSelectFirstCicp()
        {
            byte[] profile = [.. "rICC"u8, 1, 2, 3, 4];
            byte[] file = await Mp4ColorTestData.ReplaceAsync(profile, Mp4ColorTestData.Cicp(),
                Mp4ColorTestData.Cicp(primaries: 9, transfer: 16, matrix: 9));
            MediaBufferAllocator allocator = new(128 * 1024);
            await using TrackingRandomAccessSource source = new(file);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            Mp4TrackInfo track = Assert.Single(reader.Tracks);
            Assert.Equal(3, track.ColorDescriptions.Count);
            IccColorDescription icc = Assert.IsType<IccColorDescription>(track.ColorDescriptions[0]);
            Assert.True(icc.Restricted);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, icc.Profile.ToArray());
            Assert.Same(track.ColorDescriptions[1], track.Stream.ColorDescription);
            IList<VideoColorDescription> list = Assert.IsAssignableFrom<IList<VideoColorDescription>>(track.ColorDescriptions);
            Assert.Throws<NotSupportedException>(() => list[0] = new CicpColorDescription(1, 1, 1));
            await reader.DisposeAsync();
            file.AsSpan().Clear();
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, icc.Profile.ToArray());
            Assert.Equal(1, Assert.IsType<CicpColorDescription>(track.Stream.ColorDescription).Primaries);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData("rICC", true)]
        [InlineData("prof", false)]
        public async Task IccOnlyDeclarationsRemainExplicitAndCanBeInspectedWithoutConversion(string type, bool restricted)
        {
            byte[] file = await Mp4ColorTestData.ReplaceAsync([.. System.Text.Encoding.Latin1.GetBytes(type), 1, 2, 3]);
            MediaBufferAllocator allocator = new(128 * 1024);
            await using TrackingRandomAccessSource source = new(file);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            IccColorDescription description = Assert.IsType<IccColorDescription>(Assert.Single(reader.Streams).ColorDescription);
            Assert.Equal(restricted, description.Restricted);
            Assert.Throws<NotSupportedException>(() => description.Resolve(VideoColorInfo.Srgb));
            using MediaPacket? packet = await reader.ReadPacketAsync();
            Assert.NotNull(packet);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task UnknownDescriptionsAreIgnoredWithoutHidingKnownDescriptions(bool includeKnown)
        {
            byte[][] payloads = includeKnown ? ["test"u8.ToArray(), Mp4ColorTestData.Cicp()] : ["test"u8.ToArray()];
            MediaBufferAllocator allocator = new(128 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4ColorTestData.ReplaceAsync(payloads));
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            if (includeKnown)
            {
                Assert.IsType<CicpColorDescription>(Assert.Single(reader.Streams).ColorDescription);
                Assert.Single(Assert.Single(reader.Tracks).ColorDescriptions);
            }
            else
            {
                Assert.Null(Assert.Single(reader.Streams).ColorDescription);
                Assert.Empty(Assert.Single(reader.Tracks).ColorDescriptions);
            }
        }

        [Fact]
        public async Task MalformedKnownDescriptionsFailAndReleaseMetadataStorage()
        {
            byte[][] invalid = [[], [1, 2, 3], "nclx"u8.ToArray(), Mp4ColorTestData.Cicp()[..^1],
                [.. Mp4ColorTestData.Cicp(), 0], [.. Mp4ColorTestData.Cicp("nclc"), 0],
                Mp4ColorTestData.Cicp(range: 1), "rICC"u8.ToArray(), "prof"u8.ToArray()];
            foreach (byte[] payload in invalid)
            {
                MediaBufferAllocator allocator = new(128 * 1024);
                await using TrackingRandomAccessSource source = new(await Mp4ColorTestData.ReplaceAsync(payload));
                await Assert.ThrowsAsync<InvalidDataException>(async () => await Mp4Reader.OpenAsync(source, allocator));
                Assert.Equal(0, allocator.AllocatedBytes);
                Assert.False(source.IsDisposed);
            }
        }

        [Fact]
        public async Task ReservedCicpCodesRemainInspectableWithoutBeingInterpretedAsUnknown()
        {
            MediaBufferAllocator allocator = new(128 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4ColorTestData.ReplaceAsync(Mp4ColorTestData.Cicp(primaries: 65535)));
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            CicpColorDescription description = Assert.IsType<CicpColorDescription>(Assert.Single(reader.Streams).ColorDescription);
            Assert.Equal(65535, description.Primaries);
            Assert.Throws<NotSupportedException>(() => description.Resolve(VideoColorInfo.Srgb));
        }

        [Fact]
        public async Task MovieTimelineCloningPreservesSelectedColorDescription()
        {
            byte[] file = Mp4EditTestData.Replace(await File.ReadAllBytesAsync(Mp4ColorTestData.PathFor()), false,
                (1, -1, 65536), (1000, 0, 65536));
            MediaBufferAllocator allocator = new(128 * 1024);
            await using TrackingRandomAccessSource source = new(file);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator,
                new Mp4ReadOptions { TimestampMode = Mp4TimestampMode.LinearMovie });
            EncodedStreamInfo original = Assert.Single(reader.Tracks).Stream;
            EncodedStreamInfo mapped = Assert.Single(reader.Streams);
            Assert.NotEqual(original.TimeBase, mapped.TimeBase);
            Assert.Same(original.ColorDescription, mapped.ColorDescription);
            Assert.IsType<CicpColorDescription>(mapped.ColorDescription);
        }
    }
}
