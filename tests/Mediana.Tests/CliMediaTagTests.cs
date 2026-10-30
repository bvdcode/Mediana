// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text.Json;
using Mediana.Cli;
using Mediana.Core.Metadata;

namespace Mediana.Tests
{
    [Collection("CommandLine")]
    public class CliMediaTagTests
    {
        [Theory]
        [InlineData("Mp3", "stereo-vbr-cover.mp3", "Tag \"id3v2\"/\"TIT2\": ", "MP3 reference")]
        [InlineData("Flac", "stereo-covers.flac", "Tag \"vorbis\"/\"TITLE\": ", "FLAC — Музыка 🎵")]
        [InlineData("Metadata", "mdta.mp4", "Tag \"mdta\"/\"custom.example\" locale=0x00000000: ", "Value 🧪")]
        public async Task GenericProbePrintsOriginalTextMetadata(string folder, string file, string prefix, string expected)
        {
            string input = Path.Combine(AppContext.BaseDirectory, "Data", folder, file);
            (int code, string output, string error) = await CliTests.RunAsync(["probe", input]);
            Assert.True(code == 0, error);
            string line = output.Split('\n').Single(line => line.StartsWith(prefix, StringComparison.Ordinal));
            using JsonDocument values = JsonDocument.Parse(line[prefix.Length..]);
            Assert.Equal(expected, Assert.Single(values.RootElement.EnumerateArray()).GetString());
        }

        [Fact]
        public void TagFormattingKeepsUnicodeButEscapesTerminalControlsInEveryNativeField()
        {
            MediaTag tag = new("n\n\u001b", "k\r\t", new[] { "line\nnext", "\u001b[31m", "\u009b31m", "\"quote\"\\", "Русский 🎵" }, "d\b", uint.MaxValue);
            string text = MediaTagFormatter.Format(tag);
            Assert.DoesNotContain('\n', text);
            Assert.DoesNotContain('\r', text);
            Assert.DoesNotContain('\t', text);
            Assert.DoesNotContain('\u001b', text);
            Assert.DoesNotContain('\u009b', text);
            Assert.DoesNotContain('\b', text);
            Assert.Contains("\\n", text, StringComparison.Ordinal);
            Assert.Contains("Русский", text, StringComparison.Ordinal);
            using JsonDocument values = JsonDocument.Parse(text[(text.IndexOf(": [", StringComparison.Ordinal) + 2)..]);
            Assert.Equal(tag.Values, values.RootElement.EnumerateArray().Select(value => value.GetString()));
            Assert.Contains("locale=0xffffffff", text, StringComparison.Ordinal);
        }
    }
}
