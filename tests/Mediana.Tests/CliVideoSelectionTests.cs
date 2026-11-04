// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    [Collection("CommandLine")]
    public class CliVideoSelectionTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("mediana-video-selection-").FullName;

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task InputWithoutAudioProducesTheSameVideoWithAutomaticOrExplicitSelection(bool videoOnly, bool selectStream)
        {
            string output = Path.Combine(directory, "encoded.ts");
            List<string> arguments = ["-i", Data("avc-multislice-cabac.mp4"), output,
                "--video-codec", "H264", "--width", "48", "--height", "40"];
            if (videoOnly)
            {
                arguments.Add("--video-only");
            }
            if (selectStream)
            {
                arguments.AddRange(["--stream", "0"]);
            }
            (int code, _, _) = await CliTests.RunAsync(arguments.ToArray());
            Assert.Equal(0, code);
            Assert.Equal(await File.ReadAllBytesAsync(Data("avc-multislice-cabac.scaled-48x40.ts")),
                await File.ReadAllBytesAsync(output));
            Assert.Single(Directory.GetFileSystemEntries(directory));
        }

        [Theory]
        [InlineData("avc-aac.mp4", false)]
        [InlineData("avc-mp3-44.mp4", false)]
        [InlineData("avc-mp3-22.mp4", false)]
        [InlineData("avc-aac.mp4", true)]
        public async Task ExistingAudioRequiresExplicitOmissionAndPreservesTheDestinationOnFailure(string input, bool overwrite)
        {
            string output = Path.Combine(directory, "encoded.ts");
            byte[] original = [1, 2, 3, 4];
            List<string> arguments = ["-i", Data(input), output, "--video-codec", "H264"];
            if (overwrite)
            {
                await File.WriteAllBytesAsync(output, original);
                arguments.Add("--overwrite");
            }
            (int code, _, _) = await CliTests.RunAsync(arguments.ToArray());
            Assert.Equal(3, code);
            if (overwrite)
            {
                Assert.Equal(original, await File.ReadAllBytesAsync(output));
                Assert.Single(Directory.GetFileSystemEntries(directory));
            }
            else
            {
                Assert.Empty(Directory.GetFileSystemEntries(directory));
            }
        }

        [Fact]
        public async Task ExplicitVideoOnlyCanOmitExistingAudio()
        {
            string output = Path.Combine(directory, "encoded.ts");
            (int code, _, _) = await CliTests.RunAsync(["-i", Data("avc-aac.mp4"), output,
                "--video-codec", "H264", "--video-only"]);
            Assert.Equal(0, code);
            Assert.True(new FileInfo(output).Length > 0);
            Assert.Single(Directory.GetFileSystemEntries(directory));
        }

        private static string Data(string name) => Path.Combine(AppContext.BaseDirectory, "Data", "Mp4", name);

        public void Dispose()
        {
            Directory.Delete(directory, recursive: true);
            GC.SuppressFinalize(this);
        }
    }
}
