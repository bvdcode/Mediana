// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Images.Svg;
using Mediana.IO;

namespace Mediana.Tests
{
    public class ImageProbeTests
    {
        [Theory]
        [InlineData("width='800' height='800'", 800, 800)]
        [InlineData("viewBox='0 0 42 21'", 42, 21)]
        [InlineData("width='1in' height='2in'", 96, 192)]
        [InlineData("", 300, 150)]
        public async Task SvgProbeReadsViewportWithoutInterpretingOrRenderingGeometry(string attributes, double width, double height)
        {
            byte[] xml = Encoding.UTF8.GetBytes($"<svg xmlns='http://www.w3.org/2000/svg' {attributes}><path d='invalid'/></svg>");
            await using ShortReadSource source = new(xml);
            MediaInfo info = await CreateEngine().ProbeAsync(source);
            ImageInfo image = Assert.IsType<ImageInfo>(info.Image);
            Assert.Equal("svg", image.FormatId);
            Assert.Equal(width, image.Width);
            Assert.Equal(height, image.Height);
            Assert.Equal(ImageDimensionUnit.Pixels, image.DimensionUnit);
            Assert.Null(info.Container);
            Assert.Empty(info.Streams);
            Assert.Empty(info.StreamMetadata);
            Assert.False(source.IsDisposed);
        }

        [Fact]
        public async Task SvgBomEncodingAndRandomAccessCursorArePreserved()
        {
            byte[] xml = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("<svg width='800' height='600'/>")];
            using MemoryStream memory = new(xml);
            await using RandomAccessStreamByteSource source = new(memory, leaveOpen: true);
            await source.ReadAsync(new byte[3]);
            MediaInfo info = await CreateEngine().ProbeAsync(source);
            Assert.Equal(800, info.Image!.Width);
            Assert.Equal(600, info.Image.Height);
            Assert.Equal(3, memory.Position);
            Assert.True(memory.CanRead);
        }

        [Fact]
        public async Task RegisteredImagesDoNotPreventSequentialContainerInspection()
        {
            FixtureContainerFactory factory = new();
            MediaEngine engine = new MediaEngineBuilder().AddContainer(factory).AddImageDecoder(new SvgImageDecoder()).Build();
            await using ShortReadSource source = new([.. "TEST"u8, 11, 22]);
            MediaInfo info = await engine.ProbeAsync(source);
            Assert.Equal("fixture", info.Container!.FormatId);
            Assert.Null(info.Image);
            Assert.Equal(0, factory.LastReader!.PacketReads);
            Assert.True(factory.LastReader.IsDisposed);
            Assert.False(source.IsDisposed);
        }

        [Fact]
        public async Task MissingMetadataCapabilityNeverTriggersPixelDecoding()
        {
            FixtureImageDecoder decoder = new();
            await using ShortReadSource source = new("TEST"u8.ToArray());
            NotSupportedException error = await Assert.ThrowsAsync<NotSupportedException>(async () =>
                await new MediaEngineBuilder().AddImageDecoder(decoder).Build().ProbeAsync(source));
            Assert.Contains("does not expose image metadata", error.Message, StringComparison.Ordinal);
            Assert.Equal(0, decoder.DecodeCalls);
        }

        [Fact]
        public async Task InvalidXmlRootIsRejectedWithoutTryingOtherFormats()
        {
            await using ShortReadSource source = new("<document/>"u8.ToArray());
            await Assert.ThrowsAsync<InvalidDataException>(async () => await CreateEngine().ProbeAsync(source));
        }

        [Fact]
        public async Task ProbeLimitsAndCancellationApplyWithoutTakingOwnership()
        {
            MediaEngine engine = CreateEngine();
            await using TrackingRandomAccessSource source = new("<svg/>"u8.ToArray());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await engine.ProbeAsync(source, cancellationToken: new(true)));
            await Assert.ThrowsAsync<NotSupportedException>(async () => await engine.ProbeAsync(source, new() { MaxProbeBytes = 1 }));
            await Assert.ThrowsAsync<MediaMemoryLimitException>(async () => await engine.ProbeAsync(source, new() { BufferBudgetBytes = 1 }));
            Assert.Empty(source.Reads);
            await Assert.ThrowsAsync<NotSupportedException>(async () => await engine.ProbeAsync(source, new() { MaxImageInputBytes = 1 }));
            Assert.False(source.IsDisposed);
        }

        private static MediaEngine CreateEngine() => new MediaEngineBuilder()
            .AddImageDecoder(new SvgImageDecoder(renderOptions: new() { MaximumSamples = 1 })).Build();
    }
}
