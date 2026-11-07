// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers;
using System.Security.Cryptography;
using Mediana.Core.Memory;
using Mediana.Formats.Video;

namespace Mediana.Codecs.Theora.Tests
{
    public class TheoraPictureTests
    {
        [Theory]
        [InlineData("motion")]
        [InlineData("noise")]
        [InlineData("odd420")]
        [InlineData("duplicate")]
        [InlineData("main420")]
        [InlineData("chroma422")]
        [InlineData("chroma444")]
        public async Task CompleteSequencesMatchIndependentNativePlanesAndRetainOutput(string name)
        {
            List<byte[]> packets = await TheoraTestInput.ReadPacketsAsync(name);
            TheoraConfiguration configuration = TheoraConfiguration.Read(TheoraTestInput.Configuration(packets));
            string root = Path.Combine(AppContext.BaseDirectory, "Data");
            string[] reference = (await File.ReadAllLinesAsync(Path.Combine(root, name + ".framehash")))
                .Where(line => !line.StartsWith('#')).Select(line => line.Split(',')[^1].Trim()).ToArray();
            byte[] opening = await File.ReadAllBytesAsync(Path.Combine(root, name + "-opening.yuv"));
            MediaBufferAllocator allocator = new(32 * 1024 * 1024, MemoryPool<byte>.Shared);
            for (int pass = 0; pass < 2; pass++)
            {
                MediaBuffer? retained = null;
                using (TheoraPictureDecoder decoder = new(configuration, allocator))
                {
                    for (int i = 3; i < packets.Count; i++)
                    {
                        using MediaBuffer picture = decoder.Decode(packets[i]);
                        byte[] pixels = Crop(decoder.Layout, picture.Memory.Span);
                        if (i == 3)
                        {
                            retained = picture.Retain();
                            Assert.Equal(opening.Length, pixels.Length);
                            for (int p = 0; p < pixels.Length; p++)
                            { Assert.True(opening[p] == pixels[p], $"{name}: opening pixel {p}, expected {opening[p]}, actual {pixels[p]}."); }
                        }
                        Assert.True(reference[i - 3].Equals(Convert.ToHexStringLower(SHA256.HashData(pixels)), StringComparison.OrdinalIgnoreCase), $"{name}: picture {i - 3} differs.");
                        if (i == packets.Count - 1)
                        {
                            using MediaBuffer duplicate = decoder.Decode([]);
                            Assert.True(duplicate.Memory.Span.SequenceEqual(picture.Memory.Span));
                        }
                    }
                    Assert.Equal(reference.Length, packets.Count - 3);
                    Assert.Equal(opening, Crop(decoder.Layout, retained!.Memory.Span));
                }
                Assert.True(allocator.AllocatedBytes > 0); retained!.Dispose(); Assert.Equal(0, allocator.AllocatedBytes);
            }
        }

        private static byte[] Crop(TheoraLayout layout, ReadOnlySpan<byte> pixels)
        {
            TheoraIdentification info = layout.Info;
            int chromaWidth = (info.Width + info.HorizontalSubsampling - 1) / info.HorizontalSubsampling;
            int chromaHeight = (info.Height + info.VerticalSubsampling - 1) / info.VerticalSubsampling;
            byte[] result = new byte[info.Width * info.Height + 2 * chromaWidth * chromaHeight]; int offset = 0;
            for (int plane = 0; plane < 3; plane++)
            {
                int hs = plane == 0 ? 1 : info.HorizontalSubsampling, vs = plane == 0 ? 1 : info.VerticalSubsampling;
                int width = (info.Width + hs - 1) / hs, height = (info.Height + vs - 1) / vs;
                int x = info.PictureX / hs, y = (info.CodedHeight - info.PictureY - info.Height) / vs;
                for (int row = 0; row < height; row++)
                {
                    pixels.Slice(layout.PlaneOffset(plane) + (y + row) * layout.PlaneWidth(plane) + x, width).CopyTo(result.AsSpan(offset)); offset += width;
                }
            }
            return result;
        }

    }
}
