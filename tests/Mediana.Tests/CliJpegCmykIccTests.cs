// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Png;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    [Collection("CommandLine")]
    public class CliJpegCmykIccTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("mediana-cmyk-").FullName;

        [Theory]
        [InlineData("cmyk-adobe")]
        [InlineData("cmyk-plain")]
        [InlineData("cmyk-progressive")]
        [InlineData("ycck-multiscan")]
        public async Task ConversionUsesEmbeddedProfileAndPublishesSrgbPixels(string name)
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data"), input = Path.Combine(directory, "input.bin"),
                output = Path.Combine(directory, "output.png");
            byte[] original = await File.ReadAllBytesAsync(Path.Combine(root, "Raster", name + ".jpg"));
            byte[] profile = await File.ReadAllBytesAsync(Path.Combine(root, "IccCmyk", "v2-mft2-lab.icc"));
            await File.WriteAllBytesAsync(input, JpegCmykIccTestInput.Attach(original, profile));
            (int code, _, string error) = await CliTests.RunAsync(["-i", input, output]);
            Assert.True(code == 0, error);
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, "IccCmyk", name + "-v2-mft2-lab.rgba"));
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (VideoFrame png = await new PngImageDecoder().DecodeAsync(await File.ReadAllBytesAsync(output), new(), allocator))
            {
                Assert.Equal(VideoColorInfo.Srgb, png.Format.Color);
                Assert.Equal(expected.Length, png.Data.Length);
                for (int i = 0; i < expected.Length; i++)
                { Assert.InRange(Math.Abs(expected[i] - png.Data.Span[i]), 0, (i & 3) == 3 ? 0 : 4); }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        public void Dispose()
        {
            Directory.Delete(directory, true);
            GC.SuppressFinalize(this);
        }
    }
}
