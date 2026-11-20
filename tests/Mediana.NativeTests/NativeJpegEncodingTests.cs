// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Jpeg;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.NativeTests
{
    internal static class NativeJpegEncodingTests
    {
        internal static async Task RunAsync()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data", "Raster");
            byte[] source = await File.ReadAllBytesAsync(Path.Combine(root, "qoi-reference.rgba"));
            byte[] reference = await File.ReadAllBytesAsync(Path.Combine(root, "jpeg-encode-reference.rgba"));
            MediaBufferAllocator allocator = new(source.Length + 17024);
            using (WritableMediaBuffer writable = allocator.Allocate(source.Length))
            {
                source.CopyTo(writable.Memory.Span);
                using MediaBuffer storage = writable.Publish();
                VideoFormat format = new(37, 29, VideoPixelFormat.Rgba32, VideoColorInfo.Srgb, SampleAspectRatio.Square);
                using VideoFrame input = new(format, storage, format.CreateTightLayout());
                using MemoryStream output = new();
                long length = await new JpegImageEncoder(new() { BackgroundColor = new(255, 255, 255) }).EncodeAsync(input, output, allocator);
                if (length != output.Length || allocator.AllocatedBytes != source.Length)
                {
                    throw new InvalidDataException("JPEG encoding returned an invalid length or retained workspace.");
                }
                using VideoFrame decoded = await new JpegImageDecoder().DecodeAsync(output.ToArray(), new(), new(64 * 1024));
                for (int i = 0; i < reference.Length; i++)
                {
                    if (Math.Abs(reference[i] - decoded.Data.Span[i]) > (i % 4 == 3 ? 0 : 3))
                    {
                        throw new InvalidDataException("JPEG encoded pixels differ from independent decoding.");
                    }
                }
            }
            if (allocator.AllocatedBytes != 0) { throw new InvalidDataException("JPEG encoding retained input storage."); }
            Console.WriteLine("PASS JPEG encoding: streamed baseline output, independent decoded pixels and bounded storage.");
        }
    }
}
