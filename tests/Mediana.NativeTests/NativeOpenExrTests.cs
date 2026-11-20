// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.OpenExr;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.NativeTests
{
    internal static class NativeOpenExrTests
    {
        internal static async Task RunAsync()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data", "Raster");
            foreach (string precision in new[] { "half", "float" })
            {
                byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, $"exr-{precision}.rgba-f32"));
                foreach (string compression in new[] { "none", "rle", "zip1", "zip16" })
                {
                    byte[] file = await File.ReadAllBytesAsync(Path.Combine(root, $"exr-{precision}-{compression}.exr"));
                    MediaBufferAllocator allocator = new(100_000);
                    using (VideoFrame frame = await new OpenExrImageDecoder().DecodeAsync(file, new(), allocator))
                    {
                        if (!frame.Data.Span.SequenceEqual(expected)) { throw new InvalidDataException("OpenEXR samples differ from independent float reference."); }
                        using VideoFrame display = SrgbConverter.Apply(frame, allocator);
                        if (display.Format.PixelFormat != VideoPixelFormat.Rgba32) { throw new InvalidDataException("OpenEXR display conversion failed."); }
                    }
                    if (allocator.AllocatedBytes != 0) { throw new InvalidDataException("OpenEXR retained allocation after disposal."); }
                }
            }
            byte[] multipart = await File.ReadAllBytesAsync(Path.Combine(root, "exr-multipart.exr"));
            MediaBufferAllocator partsAllocator = new(100_000);
            using VideoFrame color = await new OpenExrImageDecoder(new() { Layer = "beauty" }).DecodeAsync(multipart, new(), partsAllocator);
            using VideoFrame luma = await new OpenExrImageDecoder().DecodeAsync(multipart, new() { PageIndex = 1 }, partsAllocator);
            byte[] colorReference = await File.ReadAllBytesAsync(Path.Combine(root, "exr-multipart-0.rgba-f32"));
            byte[] lumaReference = await File.ReadAllBytesAsync(Path.Combine(root, "exr-multipart-1.rgba-f32"));
            if (!color.Data.Span.SequenceEqual(colorReference) || !luma.Data.Span.SequenceEqual(lumaReference))
            { throw new InvalidDataException("OpenEXR multipart selection differs."); }
            using VideoFrame preview = SrgbConverter.Apply(color, partsAllocator);
            Console.WriteLine("PASS OpenEXR HALF/FLOAT, NONE/RLE/ZIPS/ZIP, multipart/layers, alpha and sRGB conversion.");
        }
    }
}
