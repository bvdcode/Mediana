// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    [Collection("CommandLine")]
    public class CliCoverTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("mediana-cover-").FullName;

        [Theory]
        [InlineData("Mp3", "stereo-vbr-cover.mp3", "mp3", 0, "cover.png")]
        [InlineData("Flac", "stereo-covers.flac", "flac", 0, "cover.png")]
        [InlineData("Flac", "stereo-covers.flac", "flac", 1, "cover.jpg")]
        [InlineData("Metadata", "covers.m4a", "mp4", 0, "cover.png")]
        [InlineData("Metadata", "covers.m4a", "mp4", 1, "cover.jpg")]
        public async Task GenericAndNamedCommandsPreserveOriginalBytesWithRenamedInputs(
            string folder, string name, string format, int index, string reference)
        {
            string input = Path.Combine(directory, "source.bin");
            File.Copy(Data(folder, name), input);
            byte[] expected = await File.ReadAllBytesAsync(Data("Metadata", reference));
            foreach (bool named in new[] { false, true })
            {
                string output = Path.Combine(directory, named + ".bin");
                string[] command = named ? ["cover", format] : ["cover"];
                (int code, string text, _) = await CliTests.RunAsync([.. command, input, output, "--index", index.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
                Assert.Equal(0, code);
                Assert.Equal(expected, await File.ReadAllBytesAsync(output));
                Assert.Contains($"Picture {index}:", text, StringComparison.Ordinal);
                Assert.Contains("Live buffer bytes: 0.", text, StringComparison.Ordinal);
            }
        }

        [Theory]
        [InlineData("--overwrite")]
        [InlineData("-y")]
        public async Task ExtractionUsesTheSharedAtomicOverwritePolicy(string option)
        {
            string output = Path.Combine(directory, "cover.bin");
            await File.WriteAllBytesAsync(output, [1, 2, 3]);
            (int code, _, _) = await CliTests.RunAsync(["cover", Data("Metadata", "covers.m4a"), output, "--index", "1", option]);
            Assert.Equal(0, code);
            Assert.Equal(await File.ReadAllBytesAsync(Data("Metadata", "cover.jpg")), await File.ReadAllBytesAsync(output));
            Assert.Single(Directory.GetFiles(directory));
        }

        [Theory]
        [InlineData("Mp3", "stereo-cbr-id3v23.mp3", 0, 2)]
        [InlineData("Flac", "mono-speech.flac", 0, 2)]
        [InlineData("Metadata", "mdta.mp4", 0, 2)]
        [InlineData("Wave", "s16-stereo.wav", 0, 3)]
        [InlineData("Metadata", "covers.m4a", -1, 2)]
        [InlineData("Metadata", "covers.m4a", 2, 2)]
        public async Task InvalidSelectionDoesNotReplaceAnApprovedDestination(string folder, string name, int index, int expectedCode)
        {
            string output = Path.Combine(directory, "cover.bin");
            byte[] original = [1, 2, 3];
            await File.WriteAllBytesAsync(output, original);
            (int code, _, _) = await CliTests.RunAsync(["cover", Data(folder, name), output,
                "--index", index.ToString(System.Globalization.CultureInfo.InvariantCulture), "--overwrite"]);
            Assert.Equal(expectedCode, code);
            Assert.Equal(original, await File.ReadAllBytesAsync(output));
            Assert.Single(Directory.GetFiles(directory));
        }

        [Theory]
        [InlineData("mp3")]
        [InlineData("mp4")]
        public async Task ExplicitFormatCommandsRejectADifferentDetectedContainer(string format)
        {
            string output = Path.Combine(directory, "cover.bin");
            (int code, _, _) = await CliTests.RunAsync(["cover", format, Data("Flac", "stereo-covers.flac"), output]);
            Assert.Equal(3, code);
            Assert.Empty(Directory.GetFiles(directory));
        }

        [Theory]
        [InlineData("mp3")]
        [InlineData("flac")]
        public async Task ExternalReferencesAreNotWrittenAsImageFiles(string format)
        {
            byte[] reference = "https://example.invalid/cover.png"u8.ToArray();
            byte[] data = format switch
            {
                "mp3" => [.. Mp3TestInput.Tag(4, Mp3TestInput.MetadataFrame("APIC", [3, .. "-->"u8, 0, 3, 0, .. reference])), .. Mp3TestInput.Frame()],
                "flac" => FlacTestInput.File([1, 2, 3], (0, FlacTestInput.StreamInfo()), (6, FlacTestInput.Picture(mediaType: "-->", image: reference))),
                _ => throw new ArgumentOutOfRangeException(nameof(format))
            };
            string input = Path.Combine(directory, "input.bin");
            string output = Path.Combine(directory, "cover.png");
            await File.WriteAllBytesAsync(input, data);
            (int code, _, _) = await CliTests.RunAsync(["cover", input, output]);
            Assert.Equal(3, code);
            Assert.False(File.Exists(output));
            Assert.Single(Directory.GetFiles(directory));
        }

        [Fact]
        public async Task UnknownDeclaredImageTypeCanBeExtractedWithoutReencoding()
        {
            byte[] meta = Mp4MetadataTestData.Meta("mdir", Mp4MetadataTestData.Box("covr", Mp4MetadataTestData.Data(99, 0, [7, 8, 9])));
            string input = Path.Combine(directory, "input.bin");
            string output = Path.Combine(directory, "cover.bin");
            await File.WriteAllBytesAsync(input, await Mp4MetadataTestData.FileAsync(Mp4MetadataTestData.Box("udta", meta)));
            (int code, string text, _) = await CliTests.RunAsync(["cover", input, output]);
            Assert.Equal(0, code);
            Assert.Contains("declared unspecified", text, StringComparison.Ordinal);
            Assert.Equal(new byte[] { 7, 8, 9 }, await File.ReadAllBytesAsync(output));
        }

        private static string Data(string folder, string name) => Path.Combine(AppContext.BaseDirectory, "Data", folder, name);

        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}
