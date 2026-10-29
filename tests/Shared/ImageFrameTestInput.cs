// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Codecs.Jbig2;
using Mediana.Codecs.Jpeg2000;
using Mediana.Codecs.Png;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Images.Heif;
using Mediana.Processing;

namespace Mediana.Tests
{
    internal static class ImageFrameTestInput
    {
        public static async Task VerifyAsync(string root)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            foreach ((string name, int components, int bits) in new[]
                { ("L-3x5", 1, 8), ("RGB-3x5", 3, 8), ("RGBA-3x5", 4, 8), ("I16-3x5", 1, 16) })
            {
                byte[] input = await File.ReadAllBytesAsync(Path.Combine(root, "Jpeg2000", name + ".jp2"));
                byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, "Jpeg2000", name + ".raw"));
                Jpeg2000Image image = Jpeg2000Decoder.Decode(input);
                using VideoFrame frame = image.ToVideoFrame(allocator, SampleAspectRatio.Square);
                for (int pixel = 0; pixel < image.Width * image.Height; pixel++)
                {
                    for (int channel = 0; channel < 4; channel++)
                    {
                        int value = 255;
                        if (channel < 3 || components == 4)
                        {
                            int component = components == 1 ? 0 : channel;
                            int offset = (pixel * components + component) * (bits / 8);
                            value = bits == 16 ? (int)Math.Round(BinaryPrimitives.ReadUInt16LittleEndian(expected.AsSpan(offset, 2)) / 257.0) : expected[offset];
                        }
                        if (frame.Data.Span[pixel * 4 + channel] != value)
                        {
                            throw new InvalidDataException("JPEG 2000 RGBA differs from native component reference: " + name);
                        }
                    }
                }
                VerifyPng(frame, allocator);
            }
            byte[] packed = await File.ReadAllBytesAsync(Path.Combine(root, "Jbig2", "mmr-odd.bin"));
            Jbig2Page page = Jbig2Decoder.Decode(await File.ReadAllBytesAsync(Path.Combine(root, "Jbig2", "mmr-odd.jb2"))).Pages[0];
            using (VideoFrame frame = page.ToVideoFrame(allocator, SampleAspectRatio.Square))
            {
                for (int y = 0; y < page.Height; y++)
                {
                    for (int x = 0; x < page.Width; x++)
                    {
                        byte value = (packed[y * page.Stride + x / 8] & (128 >> (x % 8))) != 0 ? (byte)0 : (byte)255;
                        ReadOnlySpan<byte> pixel = frame.GetRow(0, y).Slice(x * 4, 4);
                        if (pixel[0] != value || pixel[1] != value || pixel[2] != value || pixel[3] != 255)
                        {
                            throw new InvalidDataException("JBIG2 RGBA has incorrect bit polarity, row layout or alpha.");
                        }
                    }
                }
                VerifyPng(frame, allocator);
            }
            HeicDocument heic = HeicDocument.Load(await File.ReadAllBytesAsync(Path.Combine(root, "Heif", "alpha-rotated.heic")));
            RgbaImage rgba = await heic.DecodePrimaryAsync();
            using (VideoFrame frame = rgba.ToVideoFrame(allocator, SampleAspectRatio.Square))
            {
                if (!frame.Data.Span.SequenceEqual(rgba.Pixels.Span))
                {
                    throw new InvalidDataException("HEIC frame conversion changed the decoded RGBA samples.");
                }
                VerifyPng(frame, allocator);
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidDataException("Image conversion retained an output buffer lease.");
            }
        }

        private static void VerifyPng(VideoFrame frame, MediaBufferAllocator allocator)
        {
            using VideoFrame srgb = SrgbConverter.Apply(frame, allocator);
            using MediaBuffer png = PngEncoder.Encode(srgb, allocator);
            PngTestInput.VerifyPreview(png.Memory.Span, srgb.Format.Width, srgb.Format.Height,
                srgb.Data.Span, srgb.Format.Width, (x, y) => (x, y), 0);
        }
    }
}
