// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using static Mediana.Tests.MatroskaAttachmentTestData;
using static Mediana.Tests.MatroskaMetadataTestData;
using static Mediana.Tests.MatroskaReaderTestData;

namespace Mediana.Tests
{
    [Collection("CommandLine")]
    public class CliAttachmentTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("mediana-attachment-").FullName;
        private static string Data(string folder, string file) => Path.Combine(AppContext.BaseDirectory, "Data", folder, file);

        [Fact]
        public async Task ExtractionRecognizesContentAndPreservesOriginalBytesWithSharedOverwritePolicy()
        {
            string input = Path.Combine(directory, "input.bin");
            string output = Path.Combine(directory, "output.bin");
            File.Copy(Data("Matroska", "avc-aac-attachment.mkv"), input);
            await File.WriteAllBytesAsync(output, [1, 2, 3]);
            (int code, string text, string error) = await CliTests.RunAsync(["attachment", input, output, "--overwrite"]);
            Assert.True(code == 0, error);
            Assert.Equal(await File.ReadAllBytesAsync(Data("Metadata", "cover.png")), await File.ReadAllBytesAsync(output));
            Assert.Contains("Attachment 0: 1236 original bytes. Live buffer bytes: 0.", text, StringComparison.Ordinal);
            Assert.Equal(2, Directory.GetFiles(directory).Length);
        }

        [Theory]
        [InlineData("Matroska", "avc-aac-attachment.mkv", -1, 2)]
        [InlineData("Matroska", "avc-aac-attachment.mkv", 1, 2)]
        [InlineData("Matroska", "mp3-low-rate.mka", 0, 2)]
        [InlineData("Mp3", "stereo-vbr-cover.mp3", 0, 3)]
        public async Task InvalidOrUnsupportedSelectionPreservesApprovedDestination(string folder, string file, int index, int expectedCode)
        {
            string output = Path.Combine(directory, "output.bin");
            byte[] original = [1, 2, 3];
            await File.WriteAllBytesAsync(output, original);
            (int code, _, _) = await CliTests.RunAsync(["attachment", Data(folder, file), output,
                "--index", index.ToString(CultureInfo.InvariantCulture), "--overwrite"]);
            Assert.Equal(expectedCode, code);
            Assert.Equal(original, await File.ReadAllBytesAsync(output));
            Assert.Single(Directory.GetFiles(directory));
        }

        [Fact]
        public async Task ArbitraryAttachmentTypesUseOnlyTheExplicitDestination()
        {
            byte[] payload = [7, 8, 9];
            byte[] document = Document(Field(0x1549A966, InfoFields), Tracks, Attachments(Entry(
                [Field(0x466E, "../embedded.ttf"u8), Field(0x4660, "font/ttf"u8), Unsigned(0x46AE, 1), Field(0x465C, payload)])));
            string input = Path.Combine(directory, "input.bin");
            string output = Path.Combine(directory, "selected.bin");
            await File.WriteAllBytesAsync(input, document);
            (int code, _, string error) = await CliTests.RunAsync(["attachment", input, output]);
            Assert.True(code == 0, error);
            Assert.Equal(payload, await File.ReadAllBytesAsync(output));
            Assert.Equal(new[] { "input.bin", "selected.bin" }, Directory.GetFiles(directory).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        }

        public void Dispose()
        {
            Directory.Delete(directory, recursive: true);
            GC.SuppressFinalize(this);
        }
    }
}
