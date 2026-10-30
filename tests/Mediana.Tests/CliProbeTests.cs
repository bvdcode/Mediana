// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Cli;

namespace Mediana.Tests
{
    [Collection("CommandLine")]
    public class CliProbeTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("mediana-probe-").FullName;

        [Theory]
        [InlineData("Mp4", "avc-aac.mp4", "mp4", "h264", "1.6")]
        [InlineData("Mp3", "stereo-vbr-cover.mp3", "mp3", "mp3", "5.773061224")]
        [InlineData("Flac", "mono-speech.flac", "flac", "flac", "3.4")]
        [InlineData("Wave", "s16-stereo.wav", "wave", "pcm", "0.137369615")]
        [InlineData("Aiff", "s16-stereo.aiff", "aiff", "pcm.aiff", "0.137369615")]
        public async Task ProbeRecognizesContentAndReportsKnownHeaderMetadata(string folder, string file, string format, string codec, string duration)
        {
            string input = Path.Combine(directory, "input.bin");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", folder, file), input);
            (int code, string output, string error) = await CliTests.RunAsync(["probe", input]);
            Assert.True(code == 0, error);
            Assert.Contains("Container: " + format + ";", output, StringComparison.Ordinal);
            Assert.Contains("; " + codec + ";", output, StringComparison.Ordinal);
            Assert.Contains("Declared duration: " + duration + " seconds.", output, StringComparison.Ordinal);
            Assert.Single(Directory.GetFiles(directory));
        }

        [Fact]
        public async Task UnknownDurationIsExplicitAndDoesNotTriggerAFullScan()
        {
            string input = Path.Combine(directory, "in.mp3");
            await File.WriteAllBytesAsync(input, [.. Mp3TestInput.Frame(), .. new byte[1024]]);
            (int code, string output, string error) = await CliTests.RunAsync(["probe", input]);
            Assert.True(code == 0, error);
            Assert.Contains("Declared duration: unknown.", output, StringComparison.Ordinal);
            Assert.Single(Directory.GetFiles(directory));
        }

        public void Dispose()
        {
            Directory.Delete(directory, recursive: true);
            GC.SuppressFinalize(this);
        }
    }
}
