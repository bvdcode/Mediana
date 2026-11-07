// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    [Collection("CommandLine")]
    public class CliProbeAliasTests
    {
        [Theory]
        [InlineData("Mp4", "avc-aac.mp4", "mp4")]
        [InlineData("Mp3", "stereo-vbr-cover.mp3", "mp3")]
        [InlineData("Flac", "mono-speech.flac", "flac")]
        public async Task ExplicitFormatCommandUsesTheSameMetadataOperation(string directory, string name, string command)
        {
            string input = Path.Combine(AppContext.BaseDirectory, "Data", directory, name);
            (int rootCode, string expected, _) = await CliTests.RunAsync(["probe", input]);
            (int code, string actual, string error) = await CliTests.RunAsync(["probe", command, input]);
            Assert.Equal(0, rootCode); Assert.True(code == 0, error); Assert.Equal(expected, actual);
        }
    }
}
