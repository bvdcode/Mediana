// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Codecs;
using Mediana.Codecs.Jbig2;
using Mediana.Codecs.Jpeg2000;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Images.Heif;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class ImageEngineTestInput
    {
        public static SampleAspectRatio Aspect { get; } = new(3, 2);
        public static MediaEngine CreateEngine() => new MediaEngineBuilder()
            .AddImageDecoder(new HeicImageDecoder(Aspect))
            .AddImageDecoder(new Jpeg2000ImageDecoder(Aspect))
            .AddImageDecoder(new Jbig2ImageDecoder(Aspect)).Build();

        public static async Task VerifyAsync(string root)
        {
            MediaEngine engine = CreateEngine();
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (JsonDocument references = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "Heif", "manifest.json"))))
            {
                foreach (JsonElement entry in references.RootElement.EnumerateArray())
                {
                    byte[] input = await File.ReadAllBytesAsync(Path.Combine(root, "Heif", entry.GetProperty("file").GetString()!));
                    CheckHash(input, entry.GetProperty("inputSha256").GetString()!);
                    using (VideoFrame frame = await DecodeAsync(engine, input, allocator))
                    {
                        VerifyFormat(frame, entry.GetProperty("width").GetInt32(), entry.GetProperty("height").GetInt32(), allocator);
                        CheckHash(frame.Data.Span, entry.GetProperty("rgbaSha256").GetString()!);
                    }
                    VerifyReleased(allocator);
                }
            }
            using (JsonDocument references = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "Jbig2", "manifest.json"))))
            {
                foreach (JsonElement entry in references.RootElement.EnumerateArray())
                {
                    string name = entry.GetProperty("name").GetString()!;
                    byte[] input = await File.ReadAllBytesAsync(Path.Combine(root, "Jbig2", name + ".jb2"));
                    byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, "Jbig2", name + ".bin"));
                    CheckHash(input, entry.GetProperty("sha256").GetString()!);
                    CheckHash(expected, entry.GetProperty("pixelSha256").GetString()!);
                    using (VideoFrame frame = await DecodeAsync(engine, input, allocator))
                    {
                        int width = entry.GetProperty("width").GetInt32();
                        int height = entry.GetProperty("height").GetInt32();
                        VerifyFormat(frame, width, height, allocator);
                        int stride = (width + 7) / 8;
                        for (int y = 0; y < height; y++)
                        {
                            for (int x = 0; x < width; x++)
                            {
                                byte value = (expected[y * stride + x / 8] & (128 >> (x % 8))) != 0 ? (byte)0 : (byte)255;
                                ReadOnlySpan<byte> pixel = frame.GetRow(0, y).Slice(x * 4, 4);
                                if (pixel[0] != value || pixel[1] != value || pixel[2] != value || pixel[3] != 255)
                                {
                                    throw new InvalidDataException("Registered JBIG2 output differs from the independent packed-page reference.");
                                }
                            }
                        }
                    }
                    VerifyReleased(allocator);
                }
            }
            byte[] white = await File.ReadAllBytesAsync(Path.Combine(root, "Jbig2", "mmr-white.jb2"));
            byte[] black = await File.ReadAllBytesAsync(Path.Combine(root, "Jbig2", "mmr-black.jb2"));
            byte[] pages = Jbig2TestInput.TwoPageFile(white, black);
            for (int index = 0; index < 2; index++)
            {
                using (VideoFrame frame = await DecodeAsync(engine, pages, allocator, new ImageDecodeRequest { PageIndex = index }))
                {
                    for (int offset = 0; offset < frame.Data.Length; offset += 4)
                    {
                        byte expected = index == 0 ? (byte)255 : (byte)0;
                        ReadOnlySpan<byte> pixel = frame.Data.Span.Slice(offset, 4);
                        if (pixel[0] != expected || pixel[1] != expected || pixel[2] != expected || pixel[3] != 255)
                        {
                            throw new InvalidDataException("The registered JBIG2 page index does not select the corresponding page association.");
                        }
                    }
                }
                VerifyReleased(allocator);
            }
            using (JsonDocument references = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "Jpeg2000", "manifest.json"))))
            {
                foreach (JsonElement entry in references.RootElement.EnumerateArray())
                {
                    string name = entry.GetProperty("name").GetString()!;
                    string kind = name.Split('-')[0];
                    if (kind is not ("L" or "I16" or "RGB" or "RGBA"))
                    {
                        continue;
                    }
                    byte[] input = await File.ReadAllBytesAsync(Path.Combine(root, "Jpeg2000", entry.GetProperty("file").GetString()!));
                    byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, "Jpeg2000", name + ".raw"));
                    CheckHash(input, entry.GetProperty("sha256").GetString()!);
                    CheckHash(expected, entry.GetProperty("rawSha256").GetString()!);
                    int components = entry.GetProperty("components").GetInt32();
                    int bits = entry.GetProperty("bits").GetInt32();
                    using (VideoFrame frame = await DecodeAsync(engine, input, allocator))
                    {
                        VerifyFormat(frame, entry.GetProperty("width").GetInt32(), entry.GetProperty("height").GetInt32(), allocator);
                        for (int pixel = 0; pixel < frame.Format.Width * frame.Format.Height; pixel++)
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
                                    throw new InvalidDataException("Registered JPEG 2000 RGBA output differs from independent integer samples.");
                                }
                            }
                        }
                    }
                    VerifyReleased(allocator);
                }
            }
        }

        private static async Task<VideoFrame> DecodeAsync(MediaEngine engine, byte[] encoded, MediaBufferAllocator allocator, ImageDecodeRequest? request = null)
        {
            using MemoryStream memory = new(encoded);
            await using RandomAccessStreamByteSource source = new(memory, leaveOpen: true);
            await source.ReadAsync(new byte[3]);
            VideoFrame frame = await engine.DecodeImageAsync(source, allocator, request);
            bool transferred = false;
            try
            {
                if (memory.Position != 3 || !memory.CanRead)
                {
                    throw new InvalidDataException("Image decoding changed the caller's source position or ownership.");
                }
                transferred = true;
                return frame;
            }
            finally
            {
                if (!transferred)
                {
                    frame.Dispose();
                }
            }
        }

        private static void VerifyFormat(VideoFrame frame, int width, int height, MediaBufferAllocator allocator)
        {
            if (frame.Format.Width != width || frame.Format.Height != height || frame.Format.PixelFormat != VideoPixelFormat.Rgba32
                || frame.Format.SampleAspectRatio != Aspect || allocator.AllocatedBytes != (long)width * height * 4)
            {
                throw new InvalidDataException("Registered image dimensions, pixel aspect or independent frame ownership differ.");
            }
        }

        private static void CheckHash(ReadOnlySpan<byte> bytes, string expected)
        {
            if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("An encoded input or independent pixel reference changed.");
            }
        }

        private static void VerifyReleased(MediaBufferAllocator allocator)
        {
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidDataException("Registered image decoding retained media buffers.");
            }
        }
    }
}
