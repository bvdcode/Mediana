// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Vp9.Tests
{
    public class Vp9SequenceTests
    {
        [Theory]
        [InlineData("main8")]
        [InlineData("adaptive")]
        [InlineData("chroma422")]
        [InlineData("chroma440")]
        [InlineData("rgb")]
        [InlineData("main12")]
        [InlineData("main10")]
        [InlineData("high8")]
        [InlineData("high12")]
        [InlineData("lossless")]
        [InlineData("resilient")]
        [InlineData("tiles")]
        [InlineData("altref")]
        public async Task EveryVisiblePictureMatchesIndependentDecoder(string name)
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data");
            byte[] ivf = await File.ReadAllBytesAsync(Path.Combine(root, name + ".ivf"));
            string[] expected = (await File.ReadAllLinesAsync(Path.Combine(root, name + ".sha256"))).Where(line => !line.StartsWith('#')).Select(line => line.Split(',')[^1].Trim().ToUpperInvariant()).ToArray();
            MediaBufferAllocator allocator = new(128 * 1024 * 1024, MemoryPool<byte>.Shared); int count = 0, coded = 0;
            using (Vp9PictureDecoder decoder = new(allocator))
            {
                for (int offset = 32; offset < ivf.Length;)
                {
                    int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(ivf.AsSpan(offset))); offset += 12;
                    ReadOnlyMemory<byte> packet = ivf.AsMemory(offset, length); offset += length;
                    Range[] ranges = new Range[8]; int frames = Vp9Superframe.Parse(packet.Span, ranges);
                    for (int i = 0; i < frames; i++, coded++)
                    {
                        (Vp9FrameHeader header, Vp9Picture picture) = decoder.Decode(packet[ranges[i]], default);
                        using (picture)
                        {
                            if (!header.ShowFrame) { continue; }
                            Assert.True(count < expected.Length, $"Unexpected picture {coded}.");
                            string actual = Hash(picture);
                            Assert.True(expected[count] == actual, $"{name}: visible {count}, coded {coded}, expected {expected[count]}, actual {actual}."); count++;
                        }
                    }
                }
            }
            Assert.Equal(expected.Length, count); Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static string Hash(Vp9Picture picture)
        {
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Vp9PictureGeometry geometry = picture.Geometry;
            int bytes = geometry.BitDepth == 8 ? 1 : 2, subX = geometry.SubsamplingX ? 1 : 0, subY = geometry.SubsamplingY ? 1 : 0;
            int yLength = picture.Stride * picture.PaddedHeight, uvLength = yLength >> (subX + subY);
            for (int plane = 0; plane < 3; plane++)
            {
                int sx = plane == 0 ? 0 : subX, sy = plane == 0 ? 0 : subY;
                int width = (geometry.Width + sx) >> sx, height = (geometry.Height + sy) >> sy;
                int start = plane switch { 0 => 0, 1 => yLength, 2 => yLength + uvLength, _ => throw new InvalidDataException() };
                for (int y = 0; y < height; y++) { hash.AppendData(picture.Pixels.Memory.Span.Slice((start + y * (picture.Stride >> sx)) * bytes, width * bytes)); }
            }
            return Convert.ToHexString(hash.GetHashAndReset());
        }
    }
}
