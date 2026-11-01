// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    [Collection("CommandLine")]
    public class CliContainerImageTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("mediana-container-image-").FullName;

        [Theory]
        [InlineData("Mp3", "stereo-vbr-cover.mp3", ContainerImageSource.Picture)]
        [InlineData("Flac", "stereo-covers.flac", ContainerImageSource.Picture)]
        [InlineData("Metadata", "covers.m4a", ContainerImageSource.Picture)]
        [InlineData("Matroska", "avc-aac-attachment.mkv", ContainerImageSource.Attachment)]
        public async Task RenamedContainersSelectOriginalPngAndReportTheActualSource(string folder, string file, ContainerImageSource source)
        {
            string input = Path.Combine(directory, "input.bin");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", folder, file), input);
            string output = Path.Combine(directory, "output.bin");
            (int code, string text, string error) = await CliTests.RunAsync(["cover", "select", input, output]);
            Assert.True(code == 0, error);
            Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Metadata", "cover.png")), await File.ReadAllBytesAsync(output));
            Assert.Contains($"Image source: {source}; index: 0;", text, StringComparison.Ordinal);
            Assert.Contains("png.managed.image", text, StringComparison.Ordinal);
            Assert.Contains("Live buffer bytes: 0.", text, StringComparison.Ordinal);
            Assert.Equal(2, Directory.GetFiles(directory).Length);
        }

        [Theory]
        [InlineData("--overwrite")]
        [InlineData("-y")]
        public async Task SelectionUsesTheSharedAtomicOverwritePolicy(string overwrite)
        {
            string output = Path.Combine(directory, "output.bin");
            await File.WriteAllBytesAsync(output, [1, 2, 3]);
            (int code, _, string error) = await CliTests.RunAsync(["cover", "select",
                Path.Combine(AppContext.BaseDirectory, "Data", "Metadata", "covers.m4a"), output, "--first", "Picture", overwrite]);
            Assert.True(code == 0, error);
            Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Metadata", "cover.png")), await File.ReadAllBytesAsync(output));
            Assert.Single(Directory.GetFiles(directory));
        }

        [Theory]
        [InlineData("Mp3", "stereo-cbr-id3v23.mp3")]
        [InlineData("Wave", "s16-stereo.wav")]
        public async Task NoRecognizedImagePreservesAnApprovedDestination(string folder, string file)
        {
            string output = Path.Combine(directory, "output.bin");
            byte[] original = [1, 2, 3];
            await File.WriteAllBytesAsync(output, original);
            (int code, _, _) = await CliTests.RunAsync(["cover", "select", Path.Combine(AppContext.BaseDirectory, "Data", folder, file), output, "--overwrite"]);
            Assert.Equal(3, code);
            Assert.Equal(original, await File.ReadAllBytesAsync(output));
            Assert.Single(Directory.GetFiles(directory));
        }

        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}
