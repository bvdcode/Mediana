// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class ContainerPictureIntegrationTests
    {
        [Fact]
        public Task RealContainersPreserveOriginalPicturesPacketsAndOwnedStorage() =>
            ContainerPictureTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data"));

        [Theory]
        [InlineData("mp3")]
        [InlineData("flac")]
        public async Task ExternalReferencesRemainDataAndPreserveOriginalOrder(string format)
        {
            byte[] reference = "https://example.invalid/cover.png"u8.ToArray();
            byte[] bytes = format switch
            {
                "mp3" => [.. Mp3TestInput.Tag(4,
                    [.. Mp3TestInput.MetadataFrame("APIC", [3, .. "-->"u8, 0, 4, .. "Remote"u8, 0, .. reference]),
                    .. Mp3TestInput.MetadataFrame("APIC", Mp3TestInput.Picture([7, 8, 9], "Embedded"))]), .. Mp3TestInput.Frame()],
                "flac" => FlacTestInput.File([1, 2, 3], (0, FlacTestInput.StreamInfo()),
                    (6, FlacTestInput.Picture(4, "-->", reference)), (6, FlacTestInput.Picture(3, image: [7, 8, 9]))),
                _ => throw new ArgumentOutOfRangeException(nameof(format))
            };
            await using ShortReadSource source = new(bytes);
            await using ContainerSession session = await ContainerPictureTestInput.CreateEngine().OpenContainerAsync(source);
            IReadOnlyList<ContainerPictureInfo> pictures = Assert.IsAssignableFrom<IReadOnlyList<ContainerPictureInfo>>(session.Pictures);
            Assert.Equal(2, pictures.Count);
            Assert.Equal(PictureDataKind.ExternalReference, pictures[0].DataKind);
            Assert.Equal("-->", pictures[0].MediaType);
            Assert.Equal(4u, pictures[0].PictureType);
            Assert.Equal(reference.Length, pictures[0].ByteLength);
            Assert.False(string.IsNullOrEmpty(pictures[0].Description));
            Assert.Equal(PictureDataKind.Embedded, pictures[1].DataKind);
            Assert.Equal(3u, pictures[1].PictureType);
            long before = session.AllocatedBufferBytes;
            using (MediaBuffer external = await session.ReadPictureAsync(0))
            using (MediaBuffer embedded = await session.ReadPictureAsync(1))
            {
                Assert.Equal(reference, external.Memory.ToArray());
                Assert.Equal(new byte[] { 7, 8, 9 }, embedded.Memory.ToArray());
                Assert.Equal(before, session.AllocatedBufferBytes);
                await session.DisposeAsync();
                Assert.Equal(reference, external.Memory.ToArray());
            }
            Assert.Equal(0, session.AllocatedBufferBytes);
            Assert.False(source.IsDisposed);
        }

        [Fact]
        public async Task UnknownMp4ImageDeclarationsRetainBytesWithoutClaimingAnImageType()
        {
            byte[] meta = Mp4MetadataTestData.Meta("mdir", Mp4MetadataTestData.Box("covr",
                Mp4MetadataTestData.Data(0x01000001, 7, [0xff, 0, 1])));
            await using TrackingRandomAccessSource source = new(await Mp4MetadataTestData.FileAsync(Mp4MetadataTestData.Box("udta", meta)), 1);
            await using ContainerSession session = await ContainerPictureTestInput.CreateEngine().OpenContainerAsync(source);
            ContainerPictureInfo info = Assert.Single(session.Pictures!);
            Assert.Equal(3, info.ByteLength);
            Assert.Null(info.MediaType);
            Assert.Null(info.Description);
            Assert.Null(info.PictureType);
            Assert.Equal(PictureDataKind.Embedded, info.DataKind);
            int reads = source.Reads.Count;
            using MediaBuffer picture = await session.ReadPictureAsync(0);
            Assert.Equal(new byte[] { 0xff, 0, 1 }, picture.Memory.ToArray());
            Assert.Equal(reads, source.Reads.Count);
        }

        [Theory]
        [InlineData("Mp3", "stereo-vbr-cover.mp3", false)]
        [InlineData("Flac", "stereo-covers.flac", false)]
        [InlineData("Metadata", "covers.m4a", false)]
        [InlineData("Mp3", "stereo-cbr-id3v23.mp3", true)]
        [InlineData("Flac", "mono-speech.flac", true)]
        [InlineData("Metadata", "mdta.mp4", true)]
        public async Task InvalidIndicesLeaveRealSessionsUsableAndListsImmutable(string folder, string file, bool empty)
        {
            byte[] data = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", folder, file));
            await using TrackingRandomAccessSource source = new(data);
            await using ContainerSession session = await ContainerPictureTestInput.CreateEngine().OpenContainerAsync(source,
                new MediaOpenOptions { MaxPacketReads = 1 });
            IReadOnlyList<ContainerPictureInfo> pictures = session.Pictures!;
            Assert.Equal(empty, pictures.Count == 0);
            if (!empty)
            {
                Assert.Throws<NotSupportedException>(() => ((IList<ContainerPictureInfo>)pictures)[0] = pictures[0]);
            }
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await session.ReadPictureAsync(-1));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await session.ReadPictureAsync(pictures.Count));
            using MediaPacket packet = Assert.IsType<MediaPacket>(await session.ReadPacketAsync());
            await session.DisposeAsync();
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await session.ReadPictureAsync(0));
            packet.Dispose();
            Assert.Equal(0, session.AllocatedBufferBytes);
        }

        [Theory]
        [InlineData("Mp3", "stereo-vbr-cover.mp3")]
        [InlineData("Flac", "stereo-covers.flac")]
        [InlineData("Metadata", "covers.m4a")]
        public async Task RetainedPicturesAccountForTheirEntireMetadataBacking(string folder, string file)
        {
            byte[] data = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", folder, file));
            await using TrackingRandomAccessSource source = new(data);
            MediaEngine engine = ContainerPictureTestInput.CreateEngine();
            await using ContainerSession session = await engine.OpenContainerAsync(source);
            long before = session.AllocatedBufferBytes;
            using (MediaBuffer picture = await session.ReadPictureAsync(0))
            {
                Assert.Equal(before, session.AllocatedBufferBytes);
                await session.DisposeAsync();
                Assert.InRange(session.AllocatedBufferBytes, picture.Memory.Length + 1L, before);
            }
            Assert.Equal(0, session.AllocatedBufferBytes);
            await Assert.ThrowsAsync<MediaMemoryLimitException>(async () => await engine.OpenContainerAsync(source,
                new MediaOpenOptions { BufferBudgetBytes = session.Pictures![0].ByteLength }));
            Assert.False(source.IsDisposed);
        }

        [Theory]
        [InlineData("Mp3", "stereo-vbr-cover.mp3")]
        [InlineData("Flac", "stereo-covers.flac")]
        [InlineData("Metadata", "covers.m4a")]
        public async Task CancellationFaultsRealSessionsAndReleasesBackingOnDisposal(string folder, string file)
        {
            byte[] data = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", folder, file));
            await using TrackingRandomAccessSource source = new(data);
            await using ContainerSession session = await ContainerPictureTestInput.CreateEngine().OpenContainerAsync(source);
            using CancellationTokenSource cancellation = new();
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await session.ReadPictureAsync(0, cancellation.Token));
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.ReadPacketAsync());
            await session.DisposeAsync();
            Assert.Equal(0, session.AllocatedBufferBytes);
            Assert.False(source.IsDisposed);
        }
    }
}
