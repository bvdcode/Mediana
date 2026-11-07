// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers;
using Mediana.Core.Memory;
using Mediana.Formats.Video;

namespace Mediana.Codecs.Mpeg4Visual.Tests
{
    public class PictureTests
    {
        [Theory]
        [InlineData("simple")]
        [InlineData("bframes")]
        [InlineData("matrix")]
        [InlineData("quarter")]
        [InlineData("motion")]
        [InlineData("noise")]
        [InlineData("fractional")]
        [InlineData("odd")]
        [InlineData("xvid")]
        public void CompletePictureSequences(string name)
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data");
            byte[] data = File.ReadAllBytes(Path.Combine(root, name + ".m4v"));
            byte[] reference = File.ReadAllBytes(Path.Combine(root, name + ".yuv"));
            Mpeg4VisualConfiguration config = Mpeg4VisualConfiguration.Read(data);
            List<int> positions = [];
            for (int i = 0; i + 4 <= data.Length; i++)
            { if (data[i] == 0 && data[i + 1] == 0 && data[i + 2] == 1 && data[i + 3] == 0xb6) { positions.Add(i + 4); } }
            int frameBytes = config.Width * config.Height + 2 * ((config.Width + 1) / 2) * ((config.Height + 1) / 2);
            MediaBufferAllocator allocator = new(32 * 1024 * 1024, MemoryPool<byte>.Shared);
            int scanned = 0;
            List<(long Time, byte[] Pixels)> pictures = [];
            using (Mpeg4PictureDecoder decoder = new(config, allocator))
            {
                for (int i = 0; i < positions.Count; i++)
                {
                    for (int cursor = scanned; cursor + 4 <= positions[i]; cursor++)
                    {
                        if (data[cursor] == 0 && data[cursor + 1] == 0 && data[cursor + 2] == 1 && data[cursor + 3] == 0xb3)
                        { decoder.ReadGroup(data.AsSpan(cursor + 4)); }
                    }
                    scanned = positions[i];
                    int end = i + 1 < positions.Count ? positions[i + 1] - 4 : data.Length;
                    using MediaBuffer picture = decoder.Decode(data.AsSpan(positions[i], end - positions[i]), out Mpeg4VopHeader header, default) ?? throw new InvalidDataException("Unexpected leading picture in a complete sequence.");
                    if (!header.IsCoded) { continue; }
                    byte[] cropped = new byte[frameBytes]; int cropAt = 0;
                    for (int plane = 0; plane < 3; plane++)
                    {
                        int stride = plane == 0 ? decoder.Width : decoder.Width / 2;
                        int width = plane == 0 ? config.Width : (config.Width + 1) / 2, height = plane == 0 ? config.Height : (config.Height + 1) / 2;
                        int offset = plane == 0 ? 0 : decoder.Width * decoder.Height + (plane - 1) * decoder.Width * decoder.Height / 4;
                        for (int y = 0; y < height; y++)
                        { picture.Memory.Span.Slice(offset + y * stride, width).CopyTo(cropped.AsSpan(cropAt)); cropAt += width; }
                    }
                    pictures.Add((decoder.PictureTime, cropped));
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Equal(reference.Length / frameBytes, pictures.Count);
            Assert.Equal(pictures.Count, pictures.Select(item => item.Time).Distinct().Count());
            int display = 0;
            foreach ((long time, byte[] pixels) in pictures.OrderBy(item => item.Time))
            {
                ReadOnlySpan<byte> expected = reference.AsSpan(display * frameBytes, frameBytes);
                long squared = 0; int maximum = 0;
                for (int at = 0; at < pixels.Length; at++)
                {
                    int error = Math.Abs(expected[at] - pixels[at]); maximum = Math.Max(maximum, error); squared += error * error;
                }
                double psnr = squared == 0 ? double.PositiveInfinity : 10 * Math.Log10(255.0 * 255 * frameBytes / squared);
                Assert.True(psnr > 48 && maximum <= 6, $"{name} display {display} time {time}: PSNR {psnr:F3} max {maximum}.");
                display++;
            }
        }
    }
}
