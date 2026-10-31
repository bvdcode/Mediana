// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    [Collection("CommandLine")]
    public class CliHlsAudioRateTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("mediana-audio-rates-").FullName;

        [Theory]
        [InlineData("96000,128000")]
        [InlineData("96000,96000")]
        public async Task ExplicitRatesProduceAlignedCompleteVariants(string rates)
        {
            string output = Path.Combine(directory, "hls");
            (int code, _, _) = await CliTests.RunAsync(["hls", "encode", Data(), output, "--audio-stream", "1",
                "--audio-codec", "Aac", "--audio-bit-rates", rates, "--segment-ms", "500", "--widths", "64", "32"]);
            Assert.Equal(0, code);
            string left = await File.ReadAllTextAsync(Path.Combine(output, "variant-64", "index.m3u8"));
            string right = await File.ReadAllTextAsync(Path.Combine(output, "variant-32", "index.m3u8"));
            Assert.Equal(left, right);
            Assert.Contains("#EXT-X-ENDLIST", left);
            string master = await File.ReadAllTextAsync(Path.Combine(output, "index.m3u8"));
            Assert.Equal(2, master.Split("mp4a.40.2").Length - 1);
        }

        [Theory]
        [InlineData("--audio-stream 1 --audio-bit-rates 96000,128000")]
        [InlineData("--video-only --audio-bit-rates 96000,128000")]
        [InlineData("--audio-stream 1 --audio-codec Aac --audio-bit-rates 96000")]
        [InlineData("--audio-stream 1 --audio-codec Aac --audio-bit-rates 96000,128000,192000")]
        [InlineData("--audio-stream 1 --audio-codec Aac --audio-bit-rates 0,128000")]
        [InlineData("--audio-stream 1 --audio-codec Aac --audio-bit-rates 96000,5000000")]
        [InlineData("--audio-stream 1 --audio-codec Aac --audio-bit-rate 128000 --audio-bit-rates 96000,128000")]
        public async Task InvalidOrConflictingRatesPublishNothing(string options)
        {
            List<string> arguments = ["hls", "encode", Data(), Path.Combine(directory, "hls")];
            arguments.AddRange(options.Split(' '));
            arguments.AddRange(["--widths", "64", "32"]);
            (int code, _, _) = await CliTests.RunAsync(arguments.ToArray());
            Assert.Equal(2, code);
            Assert.Empty(Directory.GetFileSystemEntries(directory));
        }

        private static string Data() => Path.Combine(AppContext.BaseDirectory, "Data", "Mp4", "avc-aac.mp4");

        public void Dispose()
        {
            Directory.Delete(directory, recursive: true);
            GC.SuppressFinalize(this);
        }
    }
}
