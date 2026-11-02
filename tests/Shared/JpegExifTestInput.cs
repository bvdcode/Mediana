// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text.Json;
using Mediana.Codecs;
using Mediana.Codecs.Jpeg;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    internal static class JpegExifTestInput
    {
        internal static async Task VerifyCorpusAsync(string root)
        {
            using JsonDocument corpus = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "corpus.json")));
            MediaEngine engine = new MediaEngineBuilder().AddImageDecoder(new JpegImageDecoder()).Build();
            foreach (JsonElement item in corpus.RootElement.EnumerateArray())
            {
                string name = item.GetProperty("name").GetString()!;
                byte[] input = await File.ReadAllBytesAsync(Path.Combine(root, name + ".jpg"));
                JpegImageDecoder decoder = new();
                Core.Metadata.ImageInfo info = await decoder.ReadInfoAsync(input);
                Require(info.Width == item.GetProperty("width").GetInt32() && info.Height == item.GetProperty("height").GetInt32());
                MediaBufferAllocator allocator = new(1024 * 1024);
                using (VideoFrame frame = await engine.DecodeImageAsync(input, allocator))
                {
                    Array.Clear(input);
                    byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, name + ".rgba"));
                    Require(frame.Format.Width == info.Width && frame.Format.Height == info.Height);
                    Require(frame.Format.GetColorInterpretation() == VideoColorInfo.Srgb && frame.Format.SampleAspectRatio == SampleAspectRatio.Square);
                    Require(allocator.AllocatedBytes == expected.Length && frame.Data.Length == expected.Length);
                    for (int i = 0; i < expected.Length; i++)
                    {
                        if (Math.Abs(frame.Data.Span[i] - expected[i]) > ((i & 3) == 3 ? 0 : 4))
                        {
                            throw new InvalidDataException($"Exif independent pixel mismatch: {name}, component {i}, actual {frame.Data.Span[i]}, expected {expected[i]}.");
                        }
                    }
                }
                Require(allocator.AllocatedBytes == 0);
            }
        }

        internal static async Task VerifyFailuresAsync(string root)
        {
            byte[] input = await File.ReadAllBytesAsync(Path.Combine(root, "sequential-II-6.jpg"));
            byte[] tiff = Metadata(input);
            for (int length = 0; length < tiff.Length; length++)
            {
                await RejectAsync<InvalidDataException>(ReplaceMetadata(input, tiff[..length]));
            }
            foreach (int orientation in new[] { 0, 9, 65535 })
            {
                byte[] changed = tiff.ToArray();
                BinaryPrimitives.WriteUInt16LittleEndian(changed.AsSpan(Field(changed, 274) + 8), (ushort)orientation);
                await RejectAsync<InvalidDataException>(ReplaceMetadata(input, changed));
            }
            foreach (int offset in new[] { 0, 2, Field(tiff, 274) + 2, Field(tiff, 274) + 4 })
            {
                byte[] changed = tiff.ToArray();
                changed[offset] = 0;
                await RejectAsync<InvalidDataException>(ReplaceMetadata(input, changed));
            }
            foreach (uint offset in new uint[] { 0, 1, 8, uint.MaxValue })
            {
                byte[] changed = tiff.ToArray();
                BinaryPrimitives.WriteUInt32LittleEndian(changed.AsSpan(Field(changed, 34665) + 8), offset);
                await RejectAsync<InvalidDataException>(ReplaceMetadata(input, changed));
            }
            int rational = (int)BinaryPrimitives.ReadUInt32LittleEndian(tiff.AsSpan(Field(tiff, 282) + 8));
            foreach (int part in new[] { 0, 4 })
            {
                byte[] changed = tiff.ToArray();
                changed.AsSpan(rational + part, 4).Clear();
                await RejectAsync<InvalidDataException>(ReplaceMetadata(input, changed));
            }
            byte[] unequal = tiff.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(unequal.AsSpan(rational), 73);
            await RejectAsync<InvalidDataException>(ReplaceMetadata(input, unequal));
            byte[] duplicate = tiff.ToArray();
            BinaryPrimitives.WriteUInt16LittleEndian(duplicate.AsSpan(Field(duplicate, 283)), 282);
            await RejectAsync<InvalidDataException>(ReplaceMetadata(input, duplicate));
            await RejectAsync<InvalidDataException>(JpegTestData.Insert(input, 225, [.. "Exif\0\0"u8, .. tiff]));
            byte[] jfif = [.. "JFIF\0"u8, 1, 1, 0, 0, 1, 0, 2, 0, 0];
            await RejectAsync<InvalidDataException>(JpegTestData.Insert(input, 224, jfif));
            byte[] cosited = await File.ReadAllBytesAsync(Path.Combine(root, "cosited-s2.jpg"));
            jfif[11] = 1;
            await RejectAsync<InvalidDataException>(JpegTestData.Insert(cosited, 224, jfif));
        }

        internal static async Task VerifyColorAsync(string root)
        {
            byte[] input = await File.ReadAllBytesAsync(Path.Combine(root, "sequential-II-6.jpg"));
            byte[] tiff = Metadata(input);
            int colorDirectory = (int)BinaryPrimitives.ReadUInt32LittleEndian(tiff.AsSpan(Field(tiff, 34665) + 8));
            int colorField = colorDirectory + 2 + 12;
            tiff[colorField + 8] = 255;
            tiff[colorField + 9] = 255;
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (VideoFrame frame = await new JpegImageDecoder().DecodeAsync(ReplaceMetadata(input, tiff), new(), allocator))
            {
                Require(frame.Format.ColorDescription is JpegExifColorDescription);
                bool rejected = false;
                try { frame.Format.GetColorInterpretation(); }
                catch (NotSupportedException) { rejected = true; }
                Require(rejected);
            }
            Require(allocator.AllocatedBytes == 0);
            tiff[colorField + 8] = 2;
            tiff[colorField + 9] = 0;
            await RejectAsync<InvalidDataException>(ReplaceMetadata(input, tiff));
            tiff = Metadata(input);
            BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(Field(tiff, 531)), 529);
            await RejectAsync<NotSupportedException>(ReplaceMetadata(input, tiff));
            byte[] adobe = [.. "Adobe"u8, 0, 100, 0, 0, 0, 0, 0];
            await RejectAsync<InvalidDataException>(JpegTestData.Insert(input, 238, adobe));
        }

        internal static async Task VerifyOwnershipAsync(string root)
        {
            byte[] input = await File.ReadAllBytesAsync(Path.Combine(root, "sequential-II-6.jpg"));
            JpegImageDecoder decoder = new();
            MediaBufferAllocator allocator = new(4964);
            using (VideoFrame frame = await decoder.DecodeAsync(input, new ImageDecodeRequest(), allocator))
            {
                Require(frame.Format.Width == 19 && frame.Format.Height == 35 && allocator.AllocatedBytes == 35 * 19 * 4);
            }
            Require(allocator.AllocatedBytes == 0);
            await RejectAsync<MediaMemoryLimitException>(input, 4963);
            using CancellationTokenSource cancellation = new();
            await cancellation.CancelAsync();
            bool rejected = false;
            try { await decoder.DecodeAsync(input, new(), allocator, cancellation.Token); }
            catch (OperationCanceledException) { rejected = true; }
            Require(rejected && allocator.AllocatedBytes == 0);
            using (JpegImage components = JpegDecoder.DecodeComponents(input, allocator))
            {
                Require(components.Width == 35 && components.Height == 19 && allocator.AllocatedBytes == 2304);
            }
            Require(allocator.AllocatedBytes == 0);
        }

        private static byte[] Metadata(byte[] input)
        {
            int length = BinaryPrimitives.ReadUInt16BigEndian(input.AsSpan(4));
            return input.AsSpan(12, length - 8).ToArray();
        }

        private static byte[] ReplaceMetadata(byte[] input, byte[] tiff)
        {
            int length = BinaryPrimitives.ReadUInt16BigEndian(input.AsSpan(4));
            byte[] removed = [255, 216, .. input.AsSpan(4 + length)];
            return JpegTestData.Insert(removed, 225, [.. "Exif\0\0"u8, .. tiff]);
        }

        private static int Field(byte[] tiff, int tag)
        {
            int count = BinaryPrimitives.ReadUInt16LittleEndian(tiff.AsSpan(8));
            for (int i = 0; i < count; i++)
            {
                int entry = 10 + i * 12;
                if (BinaryPrimitives.ReadUInt16LittleEndian(tiff.AsSpan(entry)) == tag) { return entry; }
            }
            throw new InvalidDataException("Missing authored Exif field.");
        }

        private static async Task RejectAsync<T>(byte[] input, long budget = 1024 * 1024) where T : Exception
        {
            MediaBufferAllocator allocator = new(budget);
            try
            {
                using VideoFrame frame = await new JpegImageDecoder().DecodeAsync(input, new(), allocator);
            }
            catch (T)
            {
                Require(allocator.AllocatedBytes == 0);
                return;
            }
            throw new InvalidDataException("An invalid Exif image, color declaration or budget was accepted.");
        }

        private static void Require(bool condition)
        {
            if (!condition) { throw new InvalidDataException("JPEG Exif presentation, metadata or ownership differs."); }
        }
    }
}
