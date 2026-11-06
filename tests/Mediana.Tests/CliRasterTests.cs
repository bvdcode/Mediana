// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Png;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    [Collection("CommandLine")]
    public class CliRasterTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("mediana-raster-").FullName;
        [Theory]
        [InlineData("bitmap-RGB.bmp", "bitmap-RGB.rgba", "bmp")]
        [InlineData("icon-png.ico", "icon-png-0.rgba", "ico")]
        [InlineData("tiff-RGBA-tiff_lzw.tif", "tiff-RGBA-tiff_lzw.rgba", "tiff")]
        [InlineData("webp-gradient-4.webp", "webp-gradient-4.rgba", "webp")]
        public async Task SignaturesSelectTheSameProbeAndConversionPipeline(string sourceName, string pixelsName, string format)
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data", "Raster");
            string input = Path.Combine(directory, "input.bin"), output = Path.Combine(directory, "output.png");
            await File.WriteAllBytesAsync(input, await File.ReadAllBytesAsync(Path.Combine(root, sourceName)));
            (int probe, string info, string probeError) = await CliTests.RunAsync(["-i", input]);
            Assert.True(probe == 0, probeError); Assert.Contains("Image: " + format, info, StringComparison.Ordinal);
            (int code, _, string error) = await CliTests.RunAsync(["-i", input, output]);
            Assert.True(code == 0, error);
            MediaBufferAllocator allocator = new(1024 * 1024);
            using VideoFrame image = await new PngImageDecoder().DecodeAsync(await File.ReadAllBytesAsync(output), new(), allocator);
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, pixelsName));
            Assert.Equal(expected, image.Data.ToArray());
        }
        [Fact]
        public async Task RawProbeUsesSensorMetadataWithoutTryingToDecodeCfaSamples()
        {
            string input = Path.Combine(AppContext.BaseDirectory, "Data", "Raster", "raw-metadata-dng.bin");
            (int code, string info, string error) = await CliTests.RunAsync(["probe", input]);
            Assert.True(code == 0, error); Assert.Contains("Image: dng; 4000x6000", info, StringComparison.Ordinal);
            Assert.Contains("DNGVersion", info, StringComparison.Ordinal);
            Assert.Contains("raw", info, StringComparison.Ordinal);
        }
        public void Dispose() => Directory.Delete(directory, true);
    }
}
