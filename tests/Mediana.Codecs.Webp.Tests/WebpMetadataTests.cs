// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Codecs.Webp;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class WebpMetadataTests
    {
        [Theory]
        [InlineData(WebpCompression.Lossless)]
        [InlineData(WebpCompression.Lossy)]
        public void ExtendedContainerPreservesOpaqueMetadataAndImagePayload(WebpCompression compression)
        {
            MediaBufferAllocator allocator = new(16 * 1024 * 1024);
            using VideoFrame frame = WebpEncodingTestInput.Frame(WebpEncodingTestInput.Pixels(33, 35, 1), 33, 35, allocator);
            WebpEncodingOptions options = new() { Compression = compression };
            using MediaBuffer baseline = WebpEncoder.Encode(frame, allocator, options);
            byte[] expectedImage = Chunk(baseline.Memory.Span, compression == WebpCompression.Lossless ? "VP8L" : "VP8 ");
            for (int mask = 0; mask < 8; mask++)
            {
                WebpMetadata metadata = new()
                {
                    IccProfile = (mask & 1) == 0 ? ReadOnlyMemory<byte>.Empty : new byte[] { 1, 2, 3 },
                    Exif = (mask & 2) == 0 ? ReadOnlyMemory<byte>.Empty : new byte[] { 4, 5, 6, 7 },
                    Xmp = (mask & 4) == 0 ? ReadOnlyMemory<byte>.Empty : new byte[] { 8, 9, 10, 11, 12 }
                };
                using MediaBuffer output = WebpEncoder.Encode(frame, allocator, options with { Metadata = metadata });
                Assert.Equal(metadata.IccProfile.ToArray(), Chunk(output.Memory.Span, "ICCP"));
                Assert.Equal(metadata.Exif.ToArray(), Chunk(output.Memory.Span, "EXIF"));
                Assert.Equal(metadata.Xmp.ToArray(), Chunk(output.Memory.Span, "XMP "));
                Assert.Equal(expectedImage, Chunk(output.Memory.Span, compression == WebpCompression.Lossless ? "VP8L" : "VP8 "));
                byte[] canvas = Chunk(output.Memory.Span, "VP8X");
                if (mask != 0 || compression == WebpCompression.Lossy)
                {
                    Assert.Equal(16 | ((mask & 1) == 0 ? 0 : 32) | ((mask & 2) == 0 ? 0 : 8) | ((mask & 4) == 0 ? 0 : 4), canvas[0]);
                }
                else { Assert.Empty(canvas); }
                string[] names = Names(output.Memory.Span);
                int imageIndex = Array.IndexOf(names, compression == WebpCompression.Lossless ? "VP8L" : "VP8 ");
                if ((mask & 1) != 0) { Assert.InRange(Array.IndexOf(names, "ICCP"), 1, imageIndex - 1); }
                if ((mask & 2) != 0) { Assert.True(Array.IndexOf(names, "EXIF") > imageIndex); }
                if ((mask & 4) != 0) { Assert.True(Array.IndexOf(names, "XMP ") > imageIndex); }
            }
        }

        private static byte[] Chunk(ReadOnlySpan<byte> file, string name)
        {
            for (int position = 12; position < file.Length;)
            {
                int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(file[(position + 4)..]));
                if (System.Text.Encoding.ASCII.GetString(file.Slice(position, 4)) == name) { return file.Slice(position + 8, length).ToArray(); }
                position += 8 + length + (length & 1);
            }
            return [];
        }

        private static string[] Names(ReadOnlySpan<byte> file)
        {
            List<string> names = [];
            for (int position = 12; position < file.Length;)
            {
                names.Add(System.Text.Encoding.ASCII.GetString(file.Slice(position, 4)));
                int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(file[(position + 4)..]));
                if ((length & 1) != 0) { Assert.Equal(0, file[position + 8 + length]); }
                position += 8 + length + (length & 1);
            }
            Assert.Equal((uint)(file.Length - 8), BinaryPrimitives.ReadUInt32LittleEndian(file[4..]));
            return names.ToArray();
        }
    }
}
