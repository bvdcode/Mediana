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
        internal const string ChromaConflictWarning = "JPEG JFIF and Exif chroma positions conflict; using centered JFIF chroma positioning.";

        internal static async Task<byte[]> ChromaConflictAsync(string root) => WithJfif(
            await File.ReadAllBytesAsync(Path.Combine(root, "cosited-s2.jpg")), true);

        internal static async Task VerifyChromaConflictAsync(string root)
        {
            JpegDecodeOptions options = new() { ChromaConflictPolicy = JpegChromaConflictPolicy.UseJfif };
            JpegImageDecoder decoder = new(options);
            MediaBufferAllocator allocator = new(1024 * 1024);
            foreach (int sampling in new[] { 1, 2 })
            {
                byte[] input = await File.ReadAllBytesAsync(Path.Combine(root, $"cosited-s{sampling}.jpg"));
                byte[] centered = Metadata(input);
                BinaryPrimitives.WriteUInt16LittleEndian(centered.AsSpan(Field(centered, 531) + 8), 1);
                using (VideoFrame expected = await new JpegImageDecoder().DecodeAsync(ReplaceMetadata(input, centered), new(), allocator))
                {
                    byte[] reference = await File.ReadAllBytesAsync(Path.Combine(root, "..", "Jpeg", $"rgb-s{sampling}-35x19.rgba"));
                    foreach (bool jfifFirst in new[] { false, true })
                    {
                        byte[] conflicting = WithJfif(input, jfifFirst);
                        await RejectAsync<InvalidDataException>(conflicting);
                        bool strictHeader = false;
                        try { await new JpegImageDecoder().ReadInfoAsync(conflicting); }
                        catch (InvalidDataException) { strictHeader = true; }
                        Require(strictHeader);
                        List<string> warnings = [];
                        Core.Metadata.ImageInfo info = await decoder.ReadInfoAsync(conflicting);
                        using VideoFrame actual = await decoder.DecodeAsync(conflicting,
                            new() { MaximumPixels = 35 * 19, WarningHandler = warnings.Add }, allocator);
                        Require(info.Width == actual.Format.Width && info.Height == actual.Format.Height);
                        Require(actual.Data.Span.SequenceEqual(expected.Data.Span) && actual.Format.Color == expected.Format.Color);
                        Require(warnings.Count == 1 && warnings[0] == ChromaConflictWarning);
                        for (int i = 0; i < reference.Length; i++)
                        {
                            Require(Math.Abs(actual.Data.Span[i] - reference[i]) <= ((i & 3) == 3 ? 0 : 4));
                        }
                    }
                }
                Require(allocator.AllocatedBytes == 0);
                List<string> unchangedWarnings = [];
                using (VideoFrame cosited = await decoder.DecodeAsync(input, new() { WarningHandler = unchangedWarnings.Add }, allocator))
                {
                    byte[] reference = await File.ReadAllBytesAsync(Path.Combine(root, $"cosited-s{sampling}.rgba"));
                    Require(unchangedWarnings.Count == 0);
                    for (int i = 0; i < reference.Length; i++)
                    {
                        Require(Math.Abs(cosited.Data.Span[i] - reference[i]) <= ((i & 3) == 3 ? 0 : 4));
                    }
                }
                Require(allocator.AllocatedBytes == 0);
            }
            byte[] conflict = await ChromaConflictAsync(root);
            byte[] reserved = Metadata(conflict);
            BinaryPrimitives.WriteUInt16LittleEndian(reserved.AsSpan(Field(reserved, 531) + 8), 3);
            await RejectAsync<InvalidDataException>(ReplaceMetadata(conflict, reserved), options: options);
            byte[] density = conflict.ToArray();
            density[JpegTestData.Find(density, 224) + 15] = 2;
            await RejectAsync<InvalidDataException>(density, options: options);
            bool stopped = false;
            try
            {
                using VideoFrame frame = await decoder.DecodeAsync(conflict,
                    new() { WarningHandler = _ => throw new InvalidOperationException("Diagnostic consumer stopped.") }, allocator);
            }
            catch (InvalidOperationException error) when (error.Message == "Diagnostic consumer stopped.") { stopped = true; }
            Require(stopped && allocator.AllocatedBytes == 0);
            byte[] restart = WithJfif(JpegTestData.Insert(JpegRestartTestInput.Create(), 225, [.. "Exif\0\0"u8, .. Metadata(conflict)]), true);
            List<string> combined = [];
            using (VideoFrame frame = await decoder.DecodeAsync(restart, new() { WarningHandler = combined.Add }, allocator))
            {
                Require(combined.Count == 2 && combined[0] == JpegRestartTestInput.Warning && combined[1] == ChromaConflictWarning);
            }
            Require(allocator.AllocatedBytes == 0);
        }

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
            int exif = JpegTestData.Find(input, 225);
            int length = BinaryPrimitives.ReadUInt16BigEndian(input.AsSpan(exif + 2));
            return input.AsSpan(exif + 10, length - 8).ToArray();
        }

        private static byte[] ReplaceMetadata(byte[] input, byte[] tiff)
        {
            int exif = JpegTestData.Find(input, 225);
            int length = BinaryPrimitives.ReadUInt16BigEndian(input.AsSpan(exif + 2));
            byte[] removed = [.. input.AsSpan(0, exif), .. input.AsSpan(exif + 2 + length)];
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

        private static byte[] WithJfif(byte[] input, bool first)
        {
            byte[] jfif = [.. "JFIF\0"u8, 1, 1, 0, 0, 1, 0, 1, 0, 0];
            if (first) { return JpegTestData.Insert(input, 224, jfif); }
            int exif = JpegTestData.Find(input, 225);
            int end = exif + 2 + BinaryPrimitives.ReadUInt16BigEndian(input.AsSpan(exif + 2));
            return [.. input.AsSpan(0, end), .. JpegTestData.Segment(224, jfif), .. input.AsSpan(end)];
        }

        private static async Task RejectAsync<T>(byte[] input, long budget = 1024 * 1024, JpegDecodeOptions? options = null) where T : Exception
        {
            MediaBufferAllocator allocator = new(budget);
            try
            {
                using VideoFrame frame = await new JpegImageDecoder(options).DecodeAsync(input, new(), allocator);
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
