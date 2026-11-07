// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Png;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Images.Heif;
using Mediana.Processing;

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

        [Theory]
        [InlineData("icc-prof-grid.heic")]
        [InlineData("icc-ricc-alpha-rotated.heic")]
        public async Task IccProfiledHeifProducesPngThroughCommonImageProcessing(string name)
        {
            string input = Path.Combine(AppContext.BaseDirectory, "Data", "Heif", name);
            string output = Path.Combine(directory, "image.png");
            (int code, _, string error) = await CliTests.RunAsync(["-i", input, output]);
            Assert.True(code == 0, error);
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (VideoFrame source = await new HeicImageDecoder(SampleAspectRatio.Square)
                .DecodeAsync(await File.ReadAllBytesAsync(input), new(), allocator))
            using (VideoFrame expected = SrgbConverter.Apply(source, allocator))
            using (VideoFrame actual = await new PngImageDecoder().DecodeAsync(await File.ReadAllBytesAsync(output), new(), allocator))
            {
                Assert.Equal(expected.Data.ToArray(), actual.Data.ToArray());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}
