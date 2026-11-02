// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Cli;

namespace Mediana.Tests
{
    [Collection("CommandLine")]
    public class CliMatroskaTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("mediana-matroska-").FullName;
        private static string Data => Path.Combine(AppContext.BaseDirectory, "Data", "Matroska", "avc-aac-attachment.mkv");

        [Fact]
        public async Task ProbeUsesFileContentAndReportsNativeFactsWithoutInventedDecodeTotals()
        {
            string input = Path.Combine(directory, "input.bin");
            File.Copy(Data, input);
            (int code, string output, string error) = await CliTests.RunAsync(["-i", input]);
            Assert.True(code == 0, error);
            Assert.Contains("Container: matroska; 2 streams.", output, StringComparison.Ordinal);
            Assert.Contains("Declared duration: 1.621 seconds.", output, StringComparison.Ordinal);
            Assert.Contains("; h264;", output, StringComparison.Ordinal);
            Assert.Contains("; aac;", output, StringComparison.Ordinal);
            Assert.Contains("Matroska stream-copy fixture", output, StringComparison.Ordinal);
            Assert.Contains("Indexed packets: 76;", output, StringComparison.Ordinal);
            Assert.DoesNotContain("indexed decode", output, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("payload bitrate:", output, StringComparison.Ordinal);
            Assert.Single(Directory.GetFiles(directory));
        }

        [Fact]
        public async Task CompleteAudioAndTimestampedPreviewMatchOriginalMedia()
        {
            string original = Path.Combine(AppContext.BaseDirectory, "Data", "Mp4", "avc-aac.mp4");
            foreach ((string extension, string[] options) in new[]
            {
                (".wav", Array.Empty<string>()), (".png", new[] { "--time-ms", "650" })
            })
            {
                string actual = Path.Combine(directory, "actual" + extension);
                string expected = Path.Combine(directory, "expected" + extension);
                (int code, _, string error) = await CliTests.RunAsync(["-i", Data, actual, .. options]);
                Assert.True(code == 0, error);
                (int referenceCode, _, string referenceError) = await CliTests.RunAsync(["-i", original, expected, .. options]);
                Assert.True(referenceCode == 0, referenceError);
                Assert.Equal(await File.ReadAllBytesAsync(expected), await File.ReadAllBytesAsync(actual));
            }
        }

        [Fact]
        public async Task UnrepresentableStreamCopyPublishesNothing()
        {
            string output = Path.Combine(directory, "output.ts");
            (int code, _, _) = await CliTests.RunAsync(["-i", Data, output]);
            Assert.Equal(CliExitCodes.Unsupported, code);
            Assert.Empty(Directory.GetFiles(directory));
        }

        public void Dispose()
        {
            Directory.Delete(directory, recursive: true);
            GC.SuppressFinalize(this);
        }
    }
}
