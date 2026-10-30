// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core;
using Mediana.Core.Metadata;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;

namespace Mediana.Tests
{
    public class H264StreamMetadataTests
    {
        [Fact]
        public async Task RealSpsPropertiesMatchIndependentReferencesWithoutDecodedSurfaces() =>
            await VideoStreamMetadataTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data"));

        [Fact]
        public void MultipleOpeningSequencesRemainIndependentAndNoSequenceIsAssumedActive() =>
            H264MetadataTestInput.VerifyMultipleConfigurations();

        [Fact]
        public void MetadataCanReadSliceGroupConfigurationThatTheDecoderRejects()
        {
            byte[] bytes = H264MetadataTestInput.Configuration([H264MetadataTestInput.Sequence()], [H264MetadataTestInput.Picture(groups: true)]);
            Assert.IsType<VideoStreamMetadata>(new H264StreamMetadataReader().Read(H264MetadataTestInput.Stream(bytes)));
            Assert.Throws<NotSupportedException>(() => H264Configuration.Parse(bytes, new H264DecoderOptions()));
        }

        [Fact]
        public async Task MetadataHasByteLimitsInsteadOfDecoderPictureAllocationLimits()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Data", "Metadata", "avc-8k.mp4");
            await using RandomAccessStreamByteSource source = new(File.OpenRead(path));
            MediaInfo info = await VideoStreamMetadataTestInput.CreateEngine().ProbeAsync(source, new MediaOpenOptions { BufferBudgetBytes = 32 * 1024 });
            Assert.Equal(7680, Assert.IsType<VideoStreamMetadata>(info.StreamMetadata[0]).Configurations[0].Width);
            Assert.Throws<NotSupportedException>(() => new H264DecoderFactory().GetUnsupportedReason(info.Streams[0]));
            Assert.Throws<NotSupportedException>(() => new H264StreamMetadataReader(new H264MetadataOptions { MaxConfigurationBytes = 7 }).Read(info.Streams[0]));
            Assert.Throws<NotSupportedException>(() => new H264StreamMetadataReader(new H264MetadataOptions { MaxParameterSetBytes = 2 }).Read(info.Streams[0]));
        }

        [Theory]
        [InlineData("avc-high10.mp4")]
        [InlineData("avc-high422.mp4")]
        [InlineData("avc-high444.mp4")]
        [InlineData("avc-interlaced.mp4")]
        public async Task UnsupportedDecodingDoesNotMakeValidAvcConfigurationMalformed(string file)
        {
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "Metadata", file)));
            MediaInfo info = await MediaProbeTestInput.CreateEngine().ProbeAsync(source);
            H264Configuration parsed = H264Configuration.Parse(info.Streams[0].Configuration.Span, new H264DecoderOptions());
            Assert.Single(parsed.Sequences);
            Assert.NotNull(new H264DecoderFactory().GetUnsupportedReason(info.Streams[0]));
            byte[] damaged = info.Streams[0].Configuration.ToArray();
            if (file is not "avc-interlaced.mp4")
            {
                damaged[^4] &= 0x3f;
                Assert.Throws<InvalidDataException>(() => new H264StreamMetadataReader().Read(H264MetadataTestInput.Stream(damaged)));
            }
        }

        [Fact]
        public void UnsupportedProfilesAndWrongStreamsAreExplicit()
        {
            H264StreamMetadataReader reader = new();
            byte[] sequence = H264MetadataTestInput.Sequence();
            sequence[1] = 255;
            Assert.Null(reader.Read(H264MetadataTestInput.Stream(H264MetadataTestInput.Configuration([sequence], [H264MetadataTestInput.Picture()]))));
            Assert.Throws<ArgumentNullException>(() => reader.Read(null!));
            Assert.Throws<ArgumentException>(() => reader.Read(new EncodedStreamInfo(0, MediaKind.Audio, "h264", new TimeBase(1, 1))));
            Assert.Throws<ArgumentException>(() => reader.Read(new EncodedStreamInfo(0, MediaKind.Video, "other", new TimeBase(1, 1))));
            Assert.Throws<ArgumentOutOfRangeException>(() => new H264StreamMetadataReader(new H264MetadataOptions { MaxConfigurationBytes = 0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new H264StreamMetadataReader(new H264MetadataOptions { MaxParameterSetBytes = 0 }));
        }

        [Fact]
        public void TruncationDuplicateIdsAndMissingReferencesDoNotReturnPartialMetadata()
        {
            H264StreamMetadataReader reader = new();
            byte[] sequence = H264MetadataTestInput.Sequence();
            byte[] picture = H264MetadataTestInput.Picture();
            byte[] bytes = H264MetadataTestInput.Configuration([sequence], [picture]);
            for (int length = 0; length < bytes.Length; length++)
            {
                Assert.Throws<InvalidDataException>(() => reader.Read(H264MetadataTestInput.Stream(bytes[..length])));
            }
            Assert.Throws<InvalidDataException>(() => reader.Read(H264MetadataTestInput.Stream(H264MetadataTestInput.Configuration([sequence, sequence], [picture]))));
            Assert.Throws<InvalidDataException>(() => reader.Read(H264MetadataTestInput.Stream(H264MetadataTestInput.Configuration([sequence], [picture, picture]))));
            Assert.Throws<InvalidDataException>(() => reader.Read(H264MetadataTestInput.Stream(H264MetadataTestInput.Configuration([sequence], [H264MetadataTestInput.Picture(sequenceId: 1)]))));
            Assert.Throws<InvalidDataException>(() => reader.Read(H264MetadataTestInput.Stream(H264MetadataTestInput.Configuration([sequence], [[0x68]]))));
        }

        [Fact]
        public void MonochromeAndMixedFieldChromaDeclarationsRemainDistinct()
        {
            byte[] mono = H264MetadataTestInput.Configuration([H264MetadataTestInput.Sequence(profile: 100, chroma: 0)], [H264MetadataTestInput.Picture()]);
            VideoStreamConfiguration monochrome = Assert.IsType<VideoStreamMetadata>(new H264StreamMetadataReader().Read(H264MetadataTestInput.Stream(mono))).Configurations[0];
            Assert.Equal(VideoChromaSampling.Monochrome, monochrome.ChromaSampling);
            Assert.Null(monochrome.ChromaBitDepth);
            string vui = "0001" + H264TestBits.Ue(2) + H264TestBits.Ue(3) + "00000";
            byte[] mixed = H264MetadataTestInput.Configuration([H264MetadataTestInput.Sequence(vui: vui)], [H264MetadataTestInput.Picture()]);
            H264SequenceMetadata value = Assert.IsType<H264SequenceMetadata>(Assert.IsType<VideoStreamMetadata>(
                new H264StreamMetadataReader().Read(H264MetadataTestInput.Stream(mixed))).Configurations[0]);
            Assert.Equal(2, value.TopFieldChromaLocation);
            Assert.Equal(3, value.BottomFieldChromaLocation);
            Assert.Equal(ChromaLocation.Unspecified, value.Color.ChromaLocation);
            Assert.Null(value.UnitsInTick);
            Assert.Null(value.FixedFrameRate);
        }

        [Fact]
        public void NativeTimingAndUnknownColorCodesSurviveTheCommonDescription()
        {
            string vui = "0 0 1 101 1 1 11111010 11111011 11111100 0 1 "
                + Convert.ToString(1001, 2).PadLeft(32, '0') + " " + Convert.ToString(60000, 2).PadLeft(32, '0') + " 1 0 0 0 0";
            byte[] bytes = H264MetadataTestInput.Configuration([H264MetadataTestInput.Sequence(frameOnly: false, vui: vui)], [H264MetadataTestInput.Picture()]);
            H264SequenceMetadata metadata = Assert.IsType<H264SequenceMetadata>(Assert.IsType<VideoStreamMetadata>(
                new H264StreamMetadataReader().Read(H264MetadataTestInput.Stream(bytes))).Configurations[0]);
            Assert.Equal(250, metadata.ColorPrimariesCode);
            Assert.Equal(251, metadata.TransferCharacteristicsCode);
            Assert.Equal(252, metadata.MatrixCoefficientsCode);
            Assert.Equal(ColorPrimaries.Unspecified, metadata.Color.Primaries);
            Assert.Equal(TransferFunction.Unspecified, metadata.Color.Transfer);
            Assert.Equal(MatrixCoefficients.Unspecified, metadata.Color.Matrix);
            Assert.Equal(ColorRange.Full, metadata.Color.Range);
            Assert.Equal(1001u, metadata.UnitsInTick);
            Assert.Equal(60000u, metadata.TimeScale);
            Assert.True(metadata.FixedFrameRate);
            Assert.False(metadata.FrameMbsOnly);
            Assert.Null(metadata.IsProgressive);
            Assert.Null(metadata.SampleAspectRatio);
        }

        [Fact]
        public async Task OneEngineCanInspectIndependentVideoInputsConcurrently()
        {
            MediaEngine engine = VideoStreamMetadataTestInput.CreateEngine();
            string directory = Path.Combine(AppContext.BaseDirectory, "Data", "Metadata");
            string[] inputs = ["avc-high10.mp4", "avc-high422.mp4", "avc-high444.mp4", "avc-8k.mp4"];
            Task[] tasks = Enumerable.Range(0, 16).Select(async index =>
            {
                await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(directory, inputs[index % inputs.Length])));
                MediaInfo info = await engine.ProbeAsync(source, new MediaOpenOptions { BufferBudgetBytes = 32 * 1024 });
                VideoStreamConfiguration value = Assert.IsType<VideoStreamMetadata>(info.StreamMetadata[0]).Configurations[0];
                Assert.Equal(index % inputs.Length == 3 ? 7680 : 96, value.Width);
                Assert.Equal(index % inputs.Length == 0 ? 10 : 8, value.LumaBitDepth);
            }).ToArray();
            await Task.WhenAll(tasks);
        }

        [Fact]
        public async Task InspectionPreservesPayloadReadRangesCancellationAndSourceLifetime()
        {
            byte[] bytes = await Mp4TestData.ReadAsync("avc-weighted.mp4");
            await using TrackingRandomAccessSource before = new(bytes, 7);
            await using TrackingRandomAccessSource after = new(bytes, 7);
            await MediaProbeTestInput.CreateEngine().ProbeAsync(before);
            MediaInfo info = await VideoStreamMetadataTestInput.CreateEngine().ProbeAsync(after);
            Assert.Equal(before.Reads, after.Reads);
            Assert.Single(info.StreamMetadata);
            Assert.False(after.IsDisposed);
            await using TrackingRandomAccessSource canceled = new(bytes);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await VideoStreamMetadataTestInput.CreateEngine()
                .ProbeAsync(canceled, cancellationToken: new CancellationToken(true)));
            Assert.Empty(canceled.Reads);
        }
    }
}
