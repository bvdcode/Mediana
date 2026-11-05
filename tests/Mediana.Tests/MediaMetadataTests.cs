// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Cli;
using Mediana.Containers;
using Mediana.Core.Metadata;
using Mediana.IO;

namespace Mediana.Tests
{
    public class MediaMetadataTests
    {
        [Fact]
        public async Task ContainerProbeRetainsExtensibleMetadataAfterReaderDisposalWithoutReadingPackets()
        {
            MediaMetadata metadata = new("container", [new("custom", "identifier", ulong.MaxValue)],
                [new("stream/0", [new("custom", "mode", "example")])]);
            FixtureContainerFactory factory = new(info: new("fixture", metadata: metadata));
            MediaEngine engine = new MediaEngineBuilder().AddContainer(factory).Build();
            MediaInfo info;
            await using (RandomAccessStreamByteSource source = new(new MemoryStream("TEST"u8.ToArray())))
            {
                info = await engine.ProbeAsync(source);
            }
            Assert.Same(metadata, info.Metadata);
            Assert.True(factory.LastReader!.IsDisposed);
            Assert.Equal(0, factory.LastReader.PacketReads);
            Assert.Equal(ulong.MaxValue, info.Metadata.Properties[0].Values[0].AsUnsignedInteger());
        }

        [Fact]
        public async Task UnknownPropertiesAndScopesUseTheSameTypedFormatterForImagesAndContainers()
        {
            MediaMetadata metadata = new("presentation", [new("ooxml", "slide_count", 7)],
                [new("slide/3", [new("custom", "title", "Example\n\"slide\""), new("custom", "duration", MediaPropertyValue.FromRational(1001, 24))])]);
            ImageInfo image = new("presentation", 720, 540, ImageDimensionUnit.Points, metadata: metadata);
            ContainerInfo container = new("video", metadata: metadata);
            using StringWriter first = new();
            using StringWriter second = new();
            await MediaMetadataFormatter.WriteAsync(image.Metadata, first, default);
            await MediaMetadataFormatter.WriteAsync(container.Metadata, second, default);
            Assert.Equal(first.ToString(), second.ToString());
            Assert.Contains("\"ooxml\"/\"slide_count\": 7", first.ToString(), StringComparison.Ordinal);
            Assert.Contains("\"custom\"/\"duration\": 1001/24", first.ToString(), StringComparison.Ordinal);
            Assert.Contains("Example\\n\\\"slide\\\"", first.ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public void ValuesPreservePrecisionAndOwnInputCollectionsAndBytes()
        {
            byte[] bytes = [1, 2, 3];
            MediaPropertyValue[] values = [ulong.MaxValue, long.MinValue, MediaPropertyValue.FromRational(1, 320), MediaPropertyValue.FromBytes(bytes), true, 0.125];
            MediaProperty property = new("example", "values", values);
            List<MediaProperty> properties = [property];
            List<MediaMetadata> children = [new("page")];
            MediaMetadata metadata = new("document", properties, children);
            Array.Clear(bytes);
            Array.Clear(values);
            properties.Clear();
            children.Clear();
            Assert.Single(metadata.Children);
            MediaProperty copy = Assert.Single(metadata.Properties);
            Assert.Equal(ulong.MaxValue, copy.Values[0].AsUnsignedInteger());
            Assert.Equal(long.MinValue, copy.Values[1].AsInteger());
            Assert.Equal((1L, 320L), copy.Values[2].AsRational());
            Assert.Equal(new byte[] { 1, 2, 3 }, copy.Values[3].AsBytes().ToArray());
            Assert.True(copy.Values[4].AsBoolean());
            Assert.Equal(0.125, copy.Values[5].AsNumber());
            Assert.Throws<InvalidOperationException>(() => copy.Values[0].AsNumber());
            Assert.Throws<ArgumentOutOfRangeException>(() => MediaPropertyValue.FromRational(1, 0));
            Assert.Throws<ArgumentException>(() => new MediaProperty("example", "undefined", default(MediaPropertyValue)));
        }
    }
}
