// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Cli;
using Mediana.Core.Metadata;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    [Collection("CommandLine")]
    public class CliVideoMetadataTests
    {
        [Theory]
        [InlineData("Metadata", "avc-high10.mp4", 96, 80, "4:2:0", 10, "progressive", "4:3")]
        [InlineData("Metadata", "avc-high422.mp4", 96, 80, "4:2:2", 8, "progressive", "4:3")]
        [InlineData("Metadata", "avc-high444.mp4", 96, 80, "4:4:4", 8, "progressive", "4:3")]
        [InlineData("Metadata", "avc-interlaced.mp4", 96, 80, "4:2:0", 8, "unknown", "4:3")]
        [InlineData("Metadata", "avc-8k.mp4", 7680, 4320, "4:2:0", 8, "progressive", "4:3")]
        [InlineData("Mp4", "avc-cropped.mp4", 78, 46, "4:2:0", 8, "progressive", "1:1")]
        [InlineData("Mp4", "avc-weighted-rotated.mp4", 94, 62, "4:2:0", 8, "progressive", "4:3")]
        public async Task ProbePrintsDeclaredSamplesAndIndexedRateWithoutDecodingOrApplyingDisplayTransforms(string folder, string file,
            int width, int height, string sampling, int depth, string scan, string aspect)
        {
            string input = Path.Combine(AppContext.BaseDirectory, "Data", folder, file);
            (int code, string output, string error) = await CliTests.RunAsync(["probe", input]);
            Assert.True(code == 0, error);
            Assert.Contains("Opening video configurations: 1.", output, StringComparison.Ordinal);
            Assert.Contains($"Configuration 1: {width}x{height} visible samples; chroma: {sampling}.", output, StringComparison.Ordinal);
            Assert.Contains($"Video precision: luma {depth} bits; chroma {depth} bits.", output, StringComparison.Ordinal);
            Assert.Contains($"Scan: {scan}.", output, StringComparison.Ordinal);
            Assert.Contains($"Sample aspect ratio: {aspect}.", output, StringComparison.Ordinal);
            Assert.Contains("Average frame rate:", output, StringComparison.Ordinal);
            MediaEngine engine = MetadataEngineFactory.Create();
            Assert.Empty(engine.AudioDecoderImplementations);
            Assert.Empty(engine.VideoDecoderImplementations);
        }

        [Fact]
        public async Task ProbePrintsDeclaredAvcAndAacProfiles()
        {
            string input = Path.Combine(AppContext.BaseDirectory, "Data", "Mp4", "avc-aac.mp4");
            (int code, string output, string error) = await CliTests.RunAsync(["probe", input]);
            Assert.True(code == 0, error);
            Assert.Contains("Codec profile: \"High\".", output, StringComparison.Ordinal);
            Assert.Contains("Codec level: \"1\".", output, StringComparison.Ordinal);
            Assert.Contains("Codec profile: \"LC\".", output, StringComparison.Ordinal);
        }

        [Fact]
        public async Task CommonFormatterKeepsProfilesWithTheirConfigurationAndOmitsUnknownLevels()
        {
            VideoStreamMetadata metadata = new([
                new VideoStreamConfiguration(16, 16, VideoChromaSampling.Monochrome, 8, null)
                    { CodecProfile = new("Baseline", "1b") },
                new VideoStreamConfiguration(32, 32, VideoChromaSampling.Monochrome, 8, null)
                    { CodecProfile = new("High") }]);
            using StringWriter output = new();
            await StreamMetadataFormatter.WriteAsync(metadata, output, default);
            string[] configurations = output.ToString().Split("  Configuration ", StringSplitOptions.None);
            Assert.Contains("Codec profile: \"Baseline\".", configurations[1], StringComparison.Ordinal);
            Assert.Contains("Codec level: \"1b\".", configurations[1], StringComparison.Ordinal);
            Assert.Contains("Codec profile: \"High\".", configurations[2], StringComparison.Ordinal);
            Assert.DoesNotContain("Codec level:", configurations[2], StringComparison.Ordinal);
        }

        [Fact]
        public async Task ProfileTextCannotInsertConsoleLinesOrControlSequences()
        {
            AudioStreamMetadata metadata = new(48000, 2) { CodecProfile = new("profile\nforged\u001b[31m", "1\rforged") };
            using StringWriter output = new();
            await StreamMetadataFormatter.WriteAsync(metadata, output, default);
            string text = output.ToString();
            Assert.DoesNotContain("\nforged", text, StringComparison.Ordinal);
            Assert.DoesNotContain("\rforged", text, StringComparison.Ordinal);
            Assert.DoesNotContain('\u001b', text);
            Assert.Contains("\\nforged", text, StringComparison.Ordinal);
            Assert.Contains("\\rforged", text, StringComparison.Ordinal);
        }

        [Fact]
        public async Task CommonFormatterPrintsEveryAlternativeAndMonochromeHasNoChromaPrecision()
        {
            VideoStreamMetadata metadata = new([
                new VideoStreamConfiguration(100, 80, VideoChromaSampling.Monochrome, 12, null, false),
                new VideoStreamConfiguration(200, 100, VideoChromaSampling.Sampling444, 10, 10, true, new SampleAspectRatio(4, 3))]);
            using StringWriter output = new();
            await StreamMetadataFormatter.WriteAsync(metadata, output, default);
            string text = output.ToString();
            Assert.Contains("Opening video configurations: 2.", text, StringComparison.Ordinal);
            Assert.Contains("Configuration 1: 100x80 visible samples; chroma: monochrome.", text, StringComparison.Ordinal);
            Assert.Contains("Configuration 2: 200x100 visible samples; chroma: 4:4:4.", text, StringComparison.Ordinal);
            Assert.Contains("Video precision: luma 12 bits.", text, StringComparison.Ordinal);
            Assert.Contains("Scan: interlaced.", text, StringComparison.Ordinal);
            Assert.Contains("Scan: progressive.", text, StringComparison.Ordinal);
            Assert.Equal(1, text.Split("Sample aspect ratio:", StringSplitOptions.None).Length - 1);
            Assert.DoesNotContain("fps", text, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ColorDescriptionRetainsHdrDeclarationsWithoutClaimingHdrDecoding()
        {
            string input = Path.Combine(AppContext.BaseDirectory, "Data", "Metadata", "avc-high10.mp4");
            (int code, string output, string error) = await CliTests.RunAsync(["probe", input]);
            Assert.True(code == 0, error);
            Assert.Contains("primaries Bt2020; transfer Pq; matrix Bt2020NonConstant; range Full; chroma location TopLeft.", output, StringComparison.Ordinal);
        }

        [Fact]
        public async Task FormattingHonorsCancellationBeforeWriting()
        {
            VideoStreamMetadata metadata = new([new VideoStreamConfiguration(10, 10, VideoChromaSampling.Monochrome, 8, null)]);
            using StringWriter output = new();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => StreamMetadataFormatter.WriteAsync(metadata, output, new CancellationToken(true)));
            Assert.Equal(string.Empty, output.ToString());
        }
    }
}
