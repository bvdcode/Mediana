// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Tests
{
    [Collection("CommandLine")]
    public class CliJpegTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("mediana-jpeg-").FullName;

        [Fact]
        public async Task JfifCorpusConvertsByContentWithCompleteIndependentPixelChecks()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data", "Jpeg");
            foreach (string line in await File.ReadAllLinesAsync(Path.Combine(root, "corpus.tsv")))
            {
                string[] values = line.Split('\t');
                if (values[0] is "embedded-cover" or "adobe-rgb")
                {
                    continue;
                }
                int width = int.Parse(values[1], System.Globalization.CultureInfo.InvariantCulture);
                int height = int.Parse(values[2], System.Globalization.CultureInfo.InvariantCulture);
                string input = Path.Combine(directory, "input.bin");
                string output = Path.Combine(directory, "output.png");
                File.Copy(Path.Combine(root, values[0] + ".jpg"), input, overwrite: true);
                (int code, _, string error) = await CliTests.RunAsync(["-i", input, output, "--overwrite"]);
                Assert.True(code == 0, error);
                byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, values[0] + ".rgba"));
                SampleAspectRatio aspect = SampleAspectRatio.Square;
                if (values[0].StartsWith("multiscan-", StringComparison.Ordinal) || values[0] == "extended-wide-quantization")
                {
                    aspect = new(3, 2);
                }
                PngTestInput.VerifyPreview(await File.ReadAllBytesAsync(output), width, height, expected, width, (x, y) => (x, y), 4, aspect);
            }
        }

        [Theory]
        [InlineData(false, "rgb-s2-35x19", 35, 19)]
        [InlineData(true, "rgb-s2-35x19", 35, 19)]
        [InlineData(false, "embedded-cover", 24, 16)]
        [InlineData(true, "embedded-cover", 24, 16)]
        public async Task ExplicitAndImplicitProbeRecognizeJpegWithoutDependingOnTheExtension(bool implicitProbe, string name, int width, int height)
        {
            string input = Path.Combine(directory, "input.bin");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Jpeg", name + ".jpg"), input);
            string[] args = implicitProbe ? ["-i", input] : ["probe", input];
            (int code, string text, string error) = await CliTests.RunAsync(args);
            Assert.True(code == 0, error);
            Assert.Contains($"Image: jpeg; {width}x{height} pixels.", text, StringComparison.Ordinal);
            Assert.Single(Directory.GetFiles(directory));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task InvalidEntropyAndUnsupportedProgressivePreserveAnApprovedOutput(bool progressive)
        {
            string input = Path.Combine(directory, "input.bin");
            string output = Path.Combine(directory, "output.png");
            byte[] encoded;
            if (progressive)
            {
                encoded = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Jpeg", "unsupported-progressive.jpg"));
            }
            else
            {
                encoded = JpegTestData.Image();
                encoded[^3] = 0;
            }
            await File.WriteAllBytesAsync(input, encoded);
            byte[] original = [9, 8, 7];
            await File.WriteAllBytesAsync(output, original);
            (int code, _, _) = await CliTests.RunAsync(["-i", input, output, "--overwrite"]);
            Assert.Equal(progressive ? 3 : 4, code);
            Assert.Equal(original, await File.ReadAllBytesAsync(output));
            Assert.Equal(2, Directory.GetFiles(directory).Length);
        }

        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}
