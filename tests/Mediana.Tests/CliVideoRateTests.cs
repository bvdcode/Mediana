// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    [Collection("CommandLine")]
    public class CliVideoRateTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("mediana-video-rate-").FullName;

        [Fact]
        public async Task StandaloneRateControlUsesTheCommonVideoEncodingPath()
        {
            string path = Path.Combine(directory, "rate.ts");
            (int code, _, _) = await CliTests.RunAsync(["-i", Data, path, "--video-codec", "H264", "--video-only",
                "--width", "48", "--video-bit-rate", "24000", "--video-buffer-ms", "300"]);
            Assert.Equal(0, code);
            Assert.True(new FileInfo(path).Length > 0);
            Assert.Single(Directory.GetFileSystemEntries(directory));
        }

        [Fact]
        public async Task PerVariantRatesPreserveAlignedHlsGroups()
        {
            string root = Path.Combine(directory, "hls");
            (int code, _, _) = await CliTests.RunAsync(["hls", "encode", Data, root, "--video-only", "--segment-ms", "500",
                "--video-bit-rates", "100000,24000", "--video-buffer-ms", "300", "--widths", "96", "48"]);
            Assert.Equal(0, code);
            string first = await File.ReadAllTextAsync(Path.Combine(root, "variant-96", "index.m3u8"));
            string second = await File.ReadAllTextAsync(Path.Combine(root, "variant-48", "index.m3u8"));
            Assert.Equal(first, second);
            Assert.Equal(6, Directory.GetFiles(Path.Combine(root, "variant-96"), "*.ts").Length);
            Assert.Equal(6, Directory.GetFiles(Path.Combine(root, "variant-48"), "*.ts").Length);
            Assert.Single(Directory.GetFileSystemEntries(directory));
        }

        [Theory]
        [InlineData("--video-buffer-ms", "100")]
        [InlineData("--video-bit-rate", "0")]
        [InlineData("--video-bit-rate", "50000001")]
        public async Task InvalidStandaloneRatePublishesNoFile(string option, string value)
        {
            (int code, _, _) = await CliTests.RunAsync(["-i", Data, Path.Combine(directory, "rate.ts"), "--video-codec", "H264",
                "--video-only", option, value]);
            Assert.NotEqual(0, code);
            Assert.Empty(Directory.GetFileSystemEntries(directory));
        }

        [Fact]
        public async Task VariantCountMismatchPublishesNoDirectory()
        {
            (int code, _, _) = await CliTests.RunAsync(["hls", "encode", Data, Path.Combine(directory, "hls"), "--video-only",
                "--video-bit-rates", "24000", "--widths", "96", "48"]);
            Assert.NotEqual(0, code);
            Assert.Empty(Directory.GetFileSystemEntries(directory));
        }

        private static string Data => Path.Combine(AppContext.BaseDirectory, "Data", "Mp4", "avc-multislice-cabac.mp4");

        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}
