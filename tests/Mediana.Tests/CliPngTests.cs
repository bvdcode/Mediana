// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Cli;
using Mediana.Core.Video;
using Mediana.IO;

namespace Mediana.Tests
{
    [Collection("CommandLine")]
    public class CliPngTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("mediana-png-").FullName;

        [Fact]
        public async Task ExplicitSvgCommandKeepsTheEmbeddedDecoderRegistry()
        {
            string input = Path.Combine(directory, "input.svg");
            string output = Path.Combine(directory, "output.png");
            byte[] png = PngDecodeTestData.Image([0, 21, 43, 65, 255], PngDecodeTestData.Chunk("sRGB", [0]));
            await File.WriteAllBytesAsync(input, EmbeddedImageTestInput.Svg(png));
            (int code, _, string error) = await CliTests.RunAsync(["image", "svg", input, output, "--width", "1"]);
            Assert.True(code == 0, error);
            PngTestInput.VerifyPreview(await File.ReadAllBytesAsync(output), 1, 1, [21, 43, 65, 255], 1, (x, y) => (x, y), 0);
        }

        [Fact]
        public async Task EveryIndependentPngReferenceConvertsByContentAndPreservesEveryOutputPixel()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data", "Png");
            string[] entries = await File.ReadAllLinesAsync(Path.Combine(root, "corpus.tsv"));
            foreach (string entry in entries)
            {
                string[] values = entry.Split('\t');
                int width = int.Parse(values[1], System.Globalization.CultureInfo.InvariantCulture);
                int height = int.Parse(values[2], System.Globalization.CultureInfo.InvariantCulture);
                string input = Path.Combine(directory, "input.bin");
                string output = Path.Combine(directory, "output.png");
                File.Copy(Path.Combine(root, values[0] + ".png"), input, overwrite: true);
                (int code, _, string error) = await CliTests.RunAsync(["-i", input, output, "--overwrite"]);
                Assert.True(code == 0, error);
                byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, values[0] + ".rgba"));
                PngTestInput.VerifyPreview(await File.ReadAllBytesAsync(output), width, height, expected, width, (x, y) => (x, y), 0, new SampleAspectRatio(3, 2));
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ProbeInspectsPngHeadersWithAnArbitraryInputExtension(bool implicitProbe)
        {
            string input = Path.Combine(directory, "source.bin");
            File.Copy(Data("Metadata", "cover.png"), input);
            string[] args = implicitProbe ? ["-i", input] : ["probe", input];
            (int code, string text, string error) = await CliTests.RunAsync(args);
            Assert.True(code == 0, error);
            Assert.Contains("Image: png; 24x16 pixels.", text, StringComparison.Ordinal);
            Assert.Single(Directory.GetFiles(directory));
        }

        [Fact]
        public async Task UntaggedPngUsesTheExistingRgbPreviewInterpretationAndLogsItOnce()
        {
            string output = Path.Combine(directory, "output.png");
            ProgressTestContext logger = new();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Data("Metadata", "cover.png")));
            int code = await ImagePreviewCommand.RunAsync(ImageEngineFactory.Create(), source, new CommandOutputFile(output, false), logger, new Mediana.Codecs.Png.PngImageEncoder());
            Assert.Equal(0, code);
            Assert.Single(logger.Messages, line => line.Contains("Missing source color metadata", StringComparison.Ordinal));
            byte[] expected = await File.ReadAllBytesAsync(Data("Png", "cover.rgba"));
            PngTestInput.VerifyPreview(await File.ReadAllBytesAsync(output), 24, 16, expected, 24, (x, y) => (x, y), 0);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task CorruptionAndUnsupportedDeclaredColorPreserveAnApprovedOutput(bool color)
        {
            string input = Path.Combine(directory, "source.bin");
            string output = Path.Combine(directory, "output.png");
            byte[] encoded = color
                ? PngDecodeTestData.Image([0, 1, 2, 3, 4], PngDecodeTestData.Chunk("gAMA", [0, 0, 177, 143]))
                : await File.ReadAllBytesAsync(Data("Metadata", "cover.png"));
            if (!color)
            {
                encoded[^1] ^= 1;
            }
            await File.WriteAllBytesAsync(input, encoded);
            byte[] original = [9, 8, 7];
            await File.WriteAllBytesAsync(output, original);
            (int code, _, _) = await CliTests.RunAsync(["-i", input, output, "--overwrite"]);
            Assert.Equal(color ? 3 : 4, code);
            Assert.Equal(original, await File.ReadAllBytesAsync(output));
            Assert.Equal(2, Directory.GetFiles(directory).Length);
        }

        private static string Data(string folder, string name) => Path.Combine(AppContext.BaseDirectory, "Data", folder, name);
        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}
