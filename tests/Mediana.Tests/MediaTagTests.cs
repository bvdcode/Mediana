// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text;
using Mediana.Containers;
using Mediana.Containers.Flac;
using Mediana.Containers.Mp3;
using Mediana.Containers.Mp4;
using Mediana.Core.Metadata;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Tests
{
    public class MediaTagTests
    {
        [Fact]
        public async Task RealHeaderTextMatchesIndependentReferencesAfterSourceDisposal() =>
            await MediaTagTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data"));

        [Fact]
        public void SnapshotsPreserveEmptyNativeTextAndDoNotExposeMutableCollections()
        {
            string[] values = ["First", "", "Русский 🎵"];
            MediaTag tag = new("native", " Title ", values, "", uint.MaxValue);
            values[0] = "changed";
            Assert.Equal(new[] { "First", "", "Русский 🎵" }, tag.Values);
            Assert.Equal(" Title ", tag.Name);
            Assert.Equal("", tag.Description);
            Assert.Equal(uint.MaxValue, tag.Locale);
            IList<string> exposedValues = Assert.IsAssignableFrom<IList<string>>(tag.Values);
            Assert.Throws<NotSupportedException>(() => exposedValues[0] = "changed");
            List<MediaTag> tags = [tag];
            ContainerInfo info = new("fixture", tags: tags);
            tags.Clear();
            Assert.Same(tag, Assert.Single(info.Tags!));
            IList<MediaTag> exposedTags = Assert.IsAssignableFrom<IList<MediaTag>>(info.Tags);
            Assert.Throws<NotSupportedException>(() => exposedTags.Clear());
            Assert.Empty(new MediaTag(" ", " ", Array.Empty<string>()).Values);
        }

        [Fact]
        public void InvalidNullItemsAndEmptyKeysAreRejected()
        {
            Assert.Throws<ArgumentException>(() => new MediaTag("", "name", "value"));
            Assert.Throws<ArgumentException>(() => new MediaTag("native", "", "value"));
            Assert.Throws<ArgumentNullException>(() => new MediaTag("native", "name", (IEnumerable<string>)null!));
            Assert.Throws<ArgumentException>(() => new MediaTag("native", "name", new string[] { null! }));
            Assert.Throws<ArgumentException>(() => new MediaTag("native", "name", (string)null!));
            Assert.Throws<ArgumentException>(() => new ContainerInfo("fixture", tags: new MediaTag[] { null! }));
        }

        [Fact]
        public async Task Id3FramesKeepNativeNamesGroupedValuesAndCustomDescriptions()
        {
            byte[] title = Mp3TestInput.MetadataFrame("TIT2", [3, .. Encoding.UTF8.GetBytes("Название 🎵")]);
            byte[] custom = Mp3TestInput.MetadataFrame("TXXX", [3, .. "catalog"u8.ToArray(), 0, .. "first"u8.ToArray(), 0, .. "second"u8.ToArray()]);
            byte[] empty = Mp3TestInput.MetadataFrame("TALB", [3]);
            byte[] picture = Mp3TestInput.MetadataFrame("APIC", Mp3TestInput.Picture([1, 2, 3]));
            byte[] body = [.. title, .. custom, .. empty, .. picture];
            MediaInfo info = await ProbeAsync([.. Mp3TestInput.Tag(4, body), .. Mp3TestInput.Frame(), .. new byte[1024]]);
            IReadOnlyList<MediaTag> tags = Assert.IsAssignableFrom<IReadOnlyList<MediaTag>>(info.Container.Tags);
            Assert.Equal(new[] { "TIT2", "TXXX", "TALB" }, tags.Select(tag => tag.Name));
            Assert.All(tags, tag => { Assert.Equal("id3v2", tag.Namespace); Assert.Null(tag.Locale); });
            Assert.Equal("Название 🎵", Assert.Single(tags[0].Values));
            Assert.Null(tags[0].Description);
            Assert.Equal("catalog", tags[1].Description);
            Assert.Equal(new[] { "first", "second" }, tags[1].Values);
            Assert.Equal("", Assert.Single(tags[2].Values));
        }

        [Fact]
        public async Task VorbisCommentsKeepRepeatedCaseSpellingEmptyValuesAndEmbeddedEquals()
        {
            byte[] file = FlacTestInput.File([0xff], (0, FlacTestInput.StreamInfo()),
                (4, FlacTestInput.Comments("ARTIST=First", "artist=Second", "EMPTY=", "EQUALS=a=b", " =space", "TITLE=Музыка 🎵")));
            MediaInfo info = await ProbeAsync(file);
            IReadOnlyList<MediaTag> tags = info.Container.Tags!;
            Assert.Equal(new[] { "ARTIST", "artist", "EMPTY", "EQUALS", " ", "TITLE" }, tags.Select(tag => tag.Name));
            Assert.Equal(new[] { "First", "Second", "", "a=b", "space", "Музыка 🎵" }, tags.Select(tag => Assert.Single(tag.Values)));
            Assert.All(tags, tag => { Assert.Equal("vorbis", tag.Namespace); Assert.Null(tag.Description); Assert.Null(tag.Locale); });
        }

        [Fact]
        public async Task Mp4TextKeepsLocalesFreeformNamespacesAndValueOrderWhileBinaryStaysNative()
        {
            byte[] localized = Mp4MetadataTestData.Box("©nam", Mp4MetadataTestData.Data(1, 0, Encoding.UTF8.GetBytes("Title 🌊")),
                Mp4MetadataTestData.Data(2, 0x474215c7, Encoding.BigEndianUnicode.GetBytes("Локальный")));
            byte[] custom = Mp4MetadataTestData.Box("----", Mp4MetadataTestData.Box("mean", new byte[4], "catalog"u8.ToArray()),
                Mp4MetadataTestData.Box("name", new byte[4], "code"u8.ToArray()), Mp4MetadataTestData.Data(1, 0, []));
            byte[] binary = Mp4MetadataTestData.Box("covr", Mp4MetadataTestData.Data(14, 0, [1, 2, 3]));
            byte[] file = await Mp4MetadataTestData.FileAsync(Mp4MetadataTestData.Box("udta",
                Mp4MetadataTestData.Meta("mdir", [.. localized, .. custom, .. binary])));
            MediaInfo info = await ProbeAsync(file);
            IReadOnlyList<MediaTag> tags = info.Container.Tags!;
            Assert.Equal(new[] { "©nam", "©nam", "code" }, tags.Select(tag => tag.Name));
            Assert.Equal(new[] { "mdir", "mdir", "catalog" }, tags.Select(tag => tag.Namespace));
            Assert.Equal(new[] { "Title 🌊", "Локальный", "" }, tags.Select(tag => Assert.Single(tag.Values)));
            Assert.Equal(new uint?[] { 0, 0x474215c7, 0 }, tags.Select(tag => tag.Locale));
            Assert.All(tags, tag => Assert.Null(tag.Description));
        }

        [Fact]
        public async Task UnavailableTextMetadataAndNoTagsFoundRemainDistinct()
        {
            MediaInfo wave = await ProbeAsync(WaveTestInput.File(("fmt ", WaveTestInput.Format()), ("data", [])));
            Assert.Null(wave.Container.Tags);
            MediaInfo mp3 = await ProbeAsync(Mp3TestInput.Frame());
            Assert.NotNull(mp3.Container.Tags);
            Assert.Empty(mp3.Container.Tags);
            MediaInfo flac = await ProbeAsync(FlacTestInput.File([], (0, FlacTestInput.StreamInfo())));
            Assert.NotNull(flac.Container.Tags);
            Assert.Empty(flac.Container.Tags);
        }

        [Fact]
        public async Task CommonProjectionPreservesContainerTextLimitsAndSourceOwnership()
        {
            byte[] body = Mp3TestInput.MetadataFrame("TIT2", [3, .. "long text"u8.ToArray()]);
            await using TrackingRandomAccessSource source = new([.. Mp3TestInput.Tag(4, body), .. Mp3TestInput.Frame()]);
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp3ReaderFactory(new Mp3ReadOptions { MaxTextBytes = 2 })).Build();
            await Assert.ThrowsAsync<NotSupportedException>(async () => await engine.ProbeAsync(source));
            Assert.False(source.IsDisposed);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await engine.ProbeAsync(source, cancellationToken: new CancellationToken(true)));
        }

        [Fact]
        public async Task FirstMetadataAccessAfterDrainAndDisposalKeepsTheOpeningHeaderSnapshot()
        {
            byte[] body = Mp3TestInput.MetadataFrame("TIT2", [3, .. "Header title"u8.ToArray()]);
            byte[] bytes = [.. Mp3TestInput.Tag(4, body), .. Mp3TestInput.Frame(), .. Mp3TestInput.Frame()];
            using MemoryStream memory = new(bytes);
            await using StreamByteSource source = new(memory, leaveOpen: true);
            MediaBufferAllocator allocator = new(128 * 1024);
            await using Mp3Reader reader = await Mp3Reader.OpenAsync(source, allocator);
            while (await reader.ReadPacketAsync() is MediaPacket packet)
            {
                packet.Dispose();
            }
            Assert.NotNull(reader.CodedDuration);
            await reader.DisposeAsync();
            ContainerInfo[] snapshots = new ContainerInfo[32];
            Parallel.For(0, snapshots.Length, index => snapshots[index] = reader.Info);
            Assert.All(snapshots, info => Assert.Same(snapshots[0], info));
            Assert.Null(snapshots[0].DeclaredDuration);
            Assert.Equal("Header title", Assert.Single(Assert.Single(snapshots[0].Tags!).Values));
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.True(memory.CanRead);
        }

        private static async Task<MediaInfo> ProbeAsync(byte[] bytes)
        {
            await using TrackingRandomAccessSource source = new(bytes, maxRead: 7);
            return await MediaProbeTestInput.CreateEngine().ProbeAsync(source);
        }
    }
}
