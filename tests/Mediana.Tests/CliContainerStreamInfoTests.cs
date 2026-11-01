// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using Mediana.Cli;
using Mediana.Containers;
using Mediana.Core.Metadata;

namespace Mediana.Tests
{
    [Collection("CommandLine")]
    public class CliContainerStreamInfoTests
    {
        [Fact]
        public async Task CommonProbePrintsThreeTrackLanguagesIndexFactsAndNativeTagScopes()
        {
            string input = Path.Combine(AppContext.BaseDirectory, "Data", "Metadata", "track-metadata.mp4");
            (int code, string output, string error) = await CliTests.RunAsync(["probe", input]);
            Assert.True(code == 0, error);
            Assert.Contains("Container: mp4; 3 streams.", output, StringComparison.Ordinal);
            Assert.Contains("Language: \"und\".", output, StringComparison.Ordinal);
            Assert.Contains("Language: \"ru-RU\".", output, StringComparison.Ordinal);
            Assert.Contains("Language: \"en-GB\".", output, StringComparison.Ordinal);
            Assert.Contains("Indexed packets:", output, StringComparison.Ordinal);
            Assert.Contains("Mean indexed payload bitrate:", output, StringComparison.Ordinal);
            Assert.Contains("source=\"trak/mdia/udta/meta\"", output, StringComparison.Ordinal);
            Assert.Contains("source=\"trak/mdia/meta\"", output, StringComparison.Ordinal);
            Assert.Contains("source=\"trak/meta\"", output, StringComparison.Ordinal);
            Assert.Contains("Русская дорожка", output, StringComparison.Ordinal);
            Assert.Contains("Основная дорожка", output, StringComparison.Ordinal);
            Assert.Equal(1, output.Split('\n').Count(line => line.Contains("Stream text tags: 3.", StringComparison.Ordinal)));
            Assert.Equal(2, output.Split('\n').Count(line => line.Contains("Stream text tags: 4.", StringComparison.Ordinal)));
            Assert.Contains("Header text tags: 5.", output, StringComparison.Ordinal);
            Assert.DoesNotContain("example.track.blob", output, StringComparison.Ordinal);
        }

        [Fact]
        public async Task CommonProbePrintsLocalizedNamesWithoutChangingTheStreamLanguage()
        {
            string input = Path.Combine(AppContext.BaseDirectory, "Data", "Metadata", "track-user-data.mp4");
            (int code, string output, string error) = await CliTests.RunAsync(["-i", input]);
            Assert.True(code == 0, error);
            Assert.Contains("Language: \"und\".", output, StringComparison.Ordinal);
            Assert.Contains("Tag \"udta\"/\"tnam\" source=\"trak/udta\" language=\"rus\": [\"Видео", output, StringComparison.Ordinal);
            Assert.Contains("Tag \"udta\"/\"tnam\" source=\"trak/mdia/udta\" language=\"fra\": [\"Nom média\"]", output, StringComparison.Ordinal);
            Assert.Contains("Tag \"udta\"/\"name\" source=\"moov/udta\": [\"Movie object\"]", output, StringComparison.Ordinal);
            Assert.DoesNotContain("zzzz", output, StringComparison.Ordinal);
        }

        [Fact]
        public async Task GenericFormatterUsesExactDurationUnitsAndInvariantRateWithoutInventingFrames()
        {
            using StringWriter output = new(CultureInfo.GetCultureInfo("ru-RU"));
            ContainerStreamInfo track = new(9, new(1000, new(1, 1000)),
                [new MediaTag("other", "name", "title", sourcePath: "stream/header")],
                new(3, 100, new(5, new(2, 3))), "en\n\u001b");
            await ContainerStreamInfoFormatter.WriteAsync(track, output, default);
            string text = output.ToString();
            Assert.Contains("Declared track duration: 1 seconds.", text, StringComparison.Ordinal);
            Assert.Contains("Indexed decode duration: 3.333333333 seconds.", text, StringComparison.Ordinal);
            Assert.Contains("Mean indexed payload bitrate: 240 bits/s.", text, StringComparison.Ordinal);
            Assert.DoesNotContain("fps", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain('\u001b', text);
            Assert.Contains("Language: \"en\\n\\u001B\".", text, StringComparison.Ordinal);
        }

        [Fact]
        public async Task ZeroDurationAndUnavailableMetadataDoNotProduceGuessedRatesOrTags()
        {
            using StringWriter output = new(CultureInfo.InvariantCulture);
            await ContainerStreamInfoFormatter.WriteAsync(new(0), output, default);
            Assert.Empty(output.ToString());
            await ContainerStreamInfoFormatter.WriteAsync(new(0, tags: [], packetIndex: new(0, 0, new(0, new(1, 1)))), output, default);
            string text = output.ToString();
            Assert.Contains("Indexed packets: 0; encoded payload: 0 bytes.", text, StringComparison.Ordinal);
            Assert.Contains("Indexed decode duration: 0 seconds.", text, StringComparison.Ordinal);
            Assert.Contains("Stream text tags: 0.", text, StringComparison.Ordinal);
            Assert.DoesNotContain("bitrate", text, StringComparison.Ordinal);
            Assert.DoesNotContain("NaN", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Infinity", text, StringComparison.Ordinal);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ContainerStreamInfoFormatter.WriteAsync(new(0), output, new CancellationToken(true)));
        }
    }
}
