// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Png;
using Mediana.Codecs.Webp;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Imaging;
using Mediana.Processing;

namespace Mediana.Tests
{
    [Collection("CommandLine")]
    public class CliIccTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("mediana-icc-").FullName;

        [Fact]
        public async Task EmbeddedImagesAndCliUseTheCommonIccTransform()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data", "Icc");
            byte[] profile = await File.ReadAllBytesAsync(Path.Combine(root, "sampled-v2.icc"));
            byte[] pixels = await File.ReadAllBytesAsync(Path.Combine(root, "sampled-v2.input.rgba"));
            MediaBufferAllocator allocator = new(16 * 1024 * 1024);
            using (VideoFrame source = IccColorTestInput.CreateFrame(profile, pixels, 289, 17, allocator))
            using (VideoFrame srgb = SrgbConverter.Apply(source, allocator))
            using (MediaBuffer encoded = WebpEncoder.Encode(srgb, allocator, new WebpEncodingOptions { Metadata = new() { IccProfile = profile } }))
            using (VideoFrame embedded = ImageResources.DecodeSynchronously(encoded.Memory, ImageResources.DefaultDecoders, new(), allocator,
                _ => throw new InvalidDataException("Color policy replaced an embedded ICC profile.")))
            {
                string input = Path.Combine(directory, "image.data"), output = Path.Combine(directory, "image.png");
                await File.WriteAllBytesAsync(input, encoded.Memory.ToArray());
                (int code, _, string error) = await CliTests.RunAsync(["-i", input, output]);
                Assert.True(code == 0, error);
                using VideoFrame decoded = await new PngImageDecoder().DecodeAsync(await File.ReadAllBytesAsync(output), new(), allocator);
                Assert.Equal(embedded.Data.ToArray(), decoded.Data.ToArray());
                Assert.Null(embedded.Format.ColorDescription);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        public void Dispose() => Directory.Delete(directory, true);
    }
}
