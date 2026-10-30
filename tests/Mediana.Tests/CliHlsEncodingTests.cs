// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;

namespace Mediana.Tests
{
    [Collection("CommandLine")]
    public class CliHlsEncodingTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("mediana-hls-").FullName;

        [Fact]
        public async Task OneInputCreatesAlignedVariantsWithMeasuredMasterMetadata()
        {
            string output = Path.Combine(directory, "hls");
            (int code, _, _) = await CliTests.RunAsync(["hls", "encode", Data("avc-multislice-cabac.mp4"), output,
                "--video-only", "--segment-ms", "500", "--widths", "96", "48"]);
            Assert.Equal(0, code);
            string master = await File.ReadAllTextAsync(Path.Combine(output, "index.m3u8"));
            Assert.Equal(2, master.Split("#EXT-X-STREAM-INF:").Length - 1);
            List<string> timelines = [];
            foreach (int width in new[] { 96, 48 })
            {
                string variant = Path.Combine(output, "variant-" + width.ToString(CultureInfo.InvariantCulture));
                string media = await File.ReadAllTextAsync(Path.Combine(variant, "index.m3u8"));
                Assert.Contains("#EXT-X-INDEPENDENT-SEGMENTS\n", media);
                Assert.Equal(6, Directory.GetFiles(variant, "*.ts").Length);
                string[] lines = media.Split('\n');
                timelines.Add(string.Join('\n', lines.Where(line => line.StartsWith("#EXTINF:", StringComparison.Ordinal))));
                Assert.Contains("RESOLUTION=" + width + "x" + (width * 5 / 6), master);
                long bytes = Directory.GetFiles(variant, "*.ts").Sum(path => new FileInfo(path).Length);
                Assert.Contains("AVERAGE-BANDWIDTH=" + (long)decimal.Ceiling(bytes * 8m / 3), master);
            }
            Assert.Equal(timelines[0], timelines[1]);
            Assert.Single(Directory.GetFileSystemEntries(directory));
        }

        [Theory]
        [InlineData("avc-multislice-cabac.mp4", true, "49 48", 2)]
        [InlineData("avc-multislice-cabac.mp4", true, "48 48", 2)]
        [InlineData("avc-multislice-cabac.mp4", true, "1", 2)]
        [InlineData("avc-multislice-cabac.mp4", true, "", 2)]
        [InlineData("avc-multislice-cabac.mp4", false, "48", 3)]
        [InlineData("avc-weighted-rotated.mp4", true, "48", 3)]
        public async Task UnsupportedOrDuplicateVariantsPublishNoDirectory(string name, bool videoOnly, string widths, int expected)
        {
            string output = Path.Combine(directory, "hls");
            List<string> arguments = ["hls", "encode", Data(name), output];
            if (videoOnly)
            {
                arguments.Add("--video-only");
            }
            if (widths.Length != 0)
            {
                arguments.Add("--widths");
                arguments.AddRange(widths.Split(' '));
            }
            (int code, _, _) = await CliTests.RunAsync(arguments.ToArray());
            Assert.Equal(expected, code);
            Assert.Empty(Directory.GetFileSystemEntries(directory));
        }

        [Fact]
        public async Task ExistingDirectoryAndItsContentSurviveRejectedOutput()
        {
            string existing = Path.Combine(directory, "hls");
            Directory.CreateDirectory(existing);
            string retained = Path.Combine(existing, "existing.bin");
            await File.WriteAllBytesAsync(retained, [1, 2, 3]);
            (int code, _, _) = await CliTests.RunAsync(["hls", "encode", Data("avc-multislice-cabac.mp4"), existing,
                "--video-only", "--widths", "48"]);
            Assert.Equal(5, code);
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(retained));
        }

        private static string Data(string name) => Path.Combine(AppContext.BaseDirectory, "Data", "Mp4", name);

        public void Dispose()
        {
            Directory.Delete(directory, recursive: true);
            GC.SuppressFinalize(this);
        }
    }
}
