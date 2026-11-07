// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Jpeg;
using Mediana.Codecs.Png;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Cli;
using Mediana.IO;
using Mediana.Processing;

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
        public async Task InvalidEntropyAndUnsupportedArithmeticPreserveAnApprovedOutput(bool arithmetic)
        {
            string input = Path.Combine(directory, "input.bin");
            string output = Path.Combine(directory, "output.png");
            byte[] encoded;
            if (arithmetic)
            {
                encoded = JpegTestData.Image();
                encoded[JpegTestData.Find(encoded, 192) + 1] = 201;
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
            Assert.Equal(arithmetic ? 3 : 4, code);
            Assert.Equal(original, await File.ReadAllBytesAsync(output));
            Assert.Equal(2, Directory.GetFiles(directory).Length);
        }

        [Fact]
        public async Task ConflictingJpegUsesJfifWithAWarningAndPublishesReferencePixels()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data", "JpegExif");
            byte[] encoded = await JpegExifTestInput.ChromaConflictAsync(root);
            string input = Path.Combine(directory, "image.data"), output = Path.Combine(directory, "image.png");
            await File.WriteAllBytesAsync(input, encoded);
            (int code, _, string error) = await CliTests.RunAsync(["-i", input, output]);
            Assert.True(code == 0, error);
            string diagnosticOutput = Path.Combine(directory, "diagnostic.png");
            ProgressTestContext logger = new();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(input));
            int diagnosticCode = await ImagePreviewCommand.RunAsync(ImageEngineFactory.Create(), source,
                new CommandOutputFile(diagnosticOutput, false), logger, new PngImageEncoder());
            Assert.Equal(0, diagnosticCode);
            Assert.Single(logger.Messages, line => line == JpegExifTestInput.ChromaConflictWarning);
            Assert.Equal(await File.ReadAllBytesAsync(output), await File.ReadAllBytesAsync(diagnosticOutput));
            MediaBufferAllocator allocator = new(1024 * 1024);
            JpegImageDecoder decoder = new(new() { ChromaConflictPolicy = JpegChromaConflictPolicy.UseJfif });
            using (VideoFrame raw = await decoder.DecodeAsync(encoded, new(), allocator))
            using (VideoFrame expected = SrgbConverter.Apply(raw, allocator))
            using (VideoFrame actual = await new PngImageDecoder().DecodeAsync(await File.ReadAllBytesAsync(output), new(), allocator))
            {
                Assert.Equal(expected.Data.ToArray(), actual.Data.ToArray());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}
