// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    [Collection("CommandLine")]
    public class CliHeifMetadataTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("mediana-heif-metadata-").FullName;

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ProbeReportsLargeImageDimensionsWithoutDecoding(bool inputOption)
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data", "Heif");
            byte[] input = await HeifMetadataTestInput.CreateAsync(root, 16320, 12240);
            string path = Path.Combine(directory, "input.bin");
            await File.WriteAllBytesAsync(path, input);
            string[] arguments = inputOption ? ["-i", path] : ["probe", path];
            (int code, string output, string error) = await CliTests.RunAsync(arguments);
            Assert.True(code == 0, error);
            Assert.Contains("Image: heif; 16320x12240 pixels.", output, StringComparison.Ordinal);
            Assert.Equal(input, await File.ReadAllBytesAsync(path));
            Assert.Equal(new[] { path }, Directory.GetFiles(directory));
        }

        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}
