// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Bmp;
using Mediana.Codecs.Gif;
using Mediana.Codecs.Tiff;
using Mediana.Codecs.Webp;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.Raster.Tests
{
    public class RasterMalformedTests
    {
        [Fact]
        public async Task TruncatedAndMutatedStreamsNeverEscapeBoundsOrRetainStorage()
        {
            (ISynchronousImageDecoder Decoder, string Name)[] cases = [
                (new BmpImageDecoder(), "bitmap-RGB.bmp"), (new GifImageDecoder(), "opaque.gif"),
                (new TiffImageDecoder(), "tiff-RGB-tiff_lzw.tif"), (new WebpImageDecoder(), "webp-regions-6.webp"),
                (new WebpImageDecoder(), "webp-lossy-noise-75.webp")];
            string root = Path.Combine(AppContext.BaseDirectory, "Data");
            Random random = new(92037);
            foreach ((ISynchronousImageDecoder decoder, string name) in cases)
            {
                byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(root, name));
                for (int iteration = 0; iteration < 80; iteration++)
                {
                    byte[] changed = (byte[])encoded.Clone();
                    if ((iteration & 1) == 0) { Array.Resize(ref changed, random.Next(changed.Length)); }
                    else { int offset = random.Next(changed.Length); changed[offset] ^= (byte)(1 << random.Next(8)); }
                    MediaBufferAllocator allocator = new(4 * 1024 * 1024);
                    Exception? failure = Record.Exception(() =>
                    {
                        using VideoFrame frame = decoder.Decode(changed, new() { MaximumPixels = 512 * 512 }, allocator);
                    });
                    Assert.False(failure is IndexOutOfRangeException or NullReferenceException or AccessViolationException, $"{name}: {failure}");
                    Assert.Equal(0, allocator.AllocatedBytes);
                }
            }
        }
    }
}
