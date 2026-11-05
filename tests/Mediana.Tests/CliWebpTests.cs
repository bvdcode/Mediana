// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Png;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    [Collection("CommandLine")]
    public class CliWebpTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("mediana-webp-").FullName;

        [Theory]
        [InlineData("svg")]
        [InlineData("pdf")]
        [InlineData("mp4")]
        [InlineData("tgs")]
        [InlineData("heif")]
        public async Task ExtensionSelectsEncoderAfterTheSameSourceFrameConversion(string kind)
        {
            string input;
            switch (kind)
            {
                case "svg":
                    input = Path.Combine(directory, "input.bin");
                    await File.WriteAllTextAsync(input, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"33\" height=\"35\"><rect width=\"25\" height=\"27\" fill=\"blue\"/></svg>");
                    break;
                case "pdf":
                    input = Path.Combine(directory, "input.bin");
                    await File.WriteAllBytesAsync(input, PdfTestDocument.Create("0 0 1 rg 10 20 30 40 re f"));
                    break;
                case "mp4": input = Data("Mp4", "avc-aac.mp4"); break;
                case "tgs": input = Data("Tgs", "orbit.tgs"); break;
                case "heif": input = Data("Heif", "basic-8bit.heic"); break;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
            string png = Path.Combine(directory, "output.png");
            string webp = Path.Combine(directory, "output.webp");
            (int pngCode, _, string pngError) = await CliTests.RunAsync(["-i", input, png]);
            (int code, _, string error) = await CliTests.RunAsync(["-i", input, webp]);
            Assert.True(pngCode == 0, pngError);
            Assert.True(code == 0, error);
            MediaBufferAllocator allocator = new(64 * 1024 * 1024);
            using VideoFrame expected = await new PngImageDecoder().DecodeAsync(await File.ReadAllBytesAsync(png), new(), allocator);
            byte[] actual = WebpLosslessTestReader.Decode(await File.ReadAllBytesAsync(webp), out int width, out int height);
            Assert.Equal(expected.Format.Width, width);
            Assert.Equal(expected.Format.Height, height);
            Assert.Equal(expected.Data.ToArray(), actual);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }

        private static string Data(string group, string name) => Path.Combine(AppContext.BaseDirectory, "Data", group, name);

        [Fact]
        public async Task EncoderWithoutAspectMetadataUsesDisplayGeometryAndTheSharedColorConversion()
        {
            string[] entries = await File.ReadAllLinesAsync(Data("Png", "corpus.tsv"));
            string name = entries[0].Split('\t')[0];
            string input = Data("Png", name + ".png");
            string output = Path.Combine(directory, "square.webp");
            MediaBufferAllocator allocator = new(64 * 1024 * 1024);
            using VideoFrame decoded = await new PngImageDecoder().DecodeAsync(await File.ReadAllBytesAsync(input), new(), allocator);
            Assert.Equal(new SampleAspectRatio(3, 2), decoded.Format.SampleAspectRatio);
            using VideoFrame expected = Mediana.Processing.SrgbPreview.Fit(decoded, VideoDisplayMatrix.Identity, int.MaxValue, int.MaxValue, allocator);
            (int code, _, string error) = await CliTests.RunAsync(["-i", input, output]);
            Assert.True(code == 0, error);
            byte[] actual = WebpLosslessTestReader.Decode(await File.ReadAllBytesAsync(output), out int width, out int height);
            Assert.Equal(expected.Format.Width, width);
            Assert.Equal(expected.Format.Height, height);
            Assert.Equal(expected.Data.ToArray(), actual);
        }

        [Theory]
        [InlineData("render")]
        [InlineData("image")]
        public async Task NamedPdfOperationsSelectTheSameRegisteredEncoder(string operation)
        {
            string input = Path.Combine(directory, "input.pdf");
            await File.WriteAllBytesAsync(input, PdfRestartTestInput.Create());
            string png = Path.Combine(directory, "output.png");
            string webp = Path.Combine(directory, "output.webp");
            (int pngCode, _, string pngError) = await CliTests.RunAsync(["pdf", operation, input, png]);
            (int code, _, string error) = await CliTests.RunAsync(["pdf", operation, input, webp]);
            Assert.True(pngCode == 0, pngError);
            Assert.True(code == 0, error);
            MediaBufferAllocator allocator = new(64 * 1024 * 1024);
            using VideoFrame expected = await new PngImageDecoder().DecodeAsync(await File.ReadAllBytesAsync(png), new(), allocator);
            Assert.Equal(expected.Data.ToArray(), WebpLosslessTestReader.Decode(await File.ReadAllBytesAsync(webp), out _, out _));
        }

        public void Dispose() => Directory.Delete(directory, true);
    }
}
