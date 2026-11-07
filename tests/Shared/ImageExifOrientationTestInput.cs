// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Codecs;
using Mediana.Codecs.Png;
using Mediana.Codecs.Webp;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Video;
using Mediana.Formats.Metadata;

namespace Mediana.Tests
{
    internal static class ImageExifOrientationTestInput
    {
        internal static byte[] Metadata(int orientation, bool little)
        {
            byte[] data = new byte[26];
            data[0] = data[1] = little ? (byte)73 : (byte)77;
            Short(2, 42); Long(4, 8); Short(8, 1);
            Short(10, 274); Short(12, 3); Long(14, 1); Short(18, (ushort)orientation);
            return data;

            void Short(int offset, ushort value)
            {
                if (little) { BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), value); }
                else { BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(offset), value); }
            }
            void Long(int offset, uint value)
            {
                if (little) { BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value); }
                else { BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset), value); }
            }
        }

        internal static byte[] WithPng(byte[] encoded, byte[] metadata) =>
            [.. encoded.AsSpan(0, 33), .. PngDecodeTestData.Chunk("eXIf", metadata), .. encoded.AsSpan(33)];

        internal static async Task VerifyPngAsync(string root)
        {
            foreach (string line in await File.ReadAllLinesAsync(Path.Combine(root, "corpus.tsv")))
            {
                string[] columns = line.Split('\t');
                byte[] input = await File.ReadAllBytesAsync(Path.Combine(root, columns[0] + ".png"));
                byte[] pixels = await File.ReadAllBytesAsync(Path.Combine(root, columns[0] + ".rgba"));
                int width = int.Parse(columns[1], System.Globalization.CultureInfo.InvariantCulture);
                int height = int.Parse(columns[2], System.Globalization.CultureInfo.InvariantCulture);
                foreach (bool little in new[] { false, true })
                {
                    for (int orientation = 1; orientation <= 8; orientation++)
                    {
                        byte[] encoded = WithPng(input, Metadata(orientation, little));
                        foreach (int factor in new[] { 1, 2, 3 })
                        {
                            PngImageDecoder decoder = new(new() { ReductionFactor = factor });
                            ImageInfo info = await decoder.ReadInfoAsync(encoded);
                            Require(info.Width == (orientation >= 5 ? height : width) && info.Height == (orientation >= 5 ? width : height));
                            // Source scanline storage is unchanged by orientation.
                            MediaBufferAllocator allocator = new(4096);
                            using (VideoFrame frame = await decoder.DecodeAsync(encoded, new(), allocator))
                            {
                                Verify(frame, pixels, width, height, orientation, factor);
                                Require(allocator.AllocatedBytes == frame.Data.Length);
                                SampleAspectRatio aspect = frame.Format.SampleAspectRatio!.Value;
                                long numerator = (long)width * 3 * ((height - 1) / factor + 1);
                                long denominator = (long)height * 2 * ((width - 1) / factor + 1);
                                if (orientation >= 5) { (numerator, denominator) = (denominator, numerator); }
                                Require(aspect.Numerator * denominator == aspect.Denominator * numerator);
                            }
                            Require(allocator.AllocatedBytes == 0);
                        }
                    }
                }
            }
            byte[] simple = PngDecodeTestData.File(PngDecodeTestData.Chunk("IHDR", PngDecodeTestData.Header(3, 2)),
                PngDecodeTestData.Chunk("IDAT", PngDecodeTestData.Compressed(new byte[26])), PngDecodeTestData.Chunk("IEND", []));
            MediaBufferAllocator exact = new(24 + 2 * 12);
            using (VideoFrame frame = await new PngImageDecoder().DecodeAsync(WithPng(simple, Metadata(6, true)), new(), exact))
            { Require(frame.Format.Width == 2 && frame.Format.Height == 3 && exact.AllocatedBytes == 24); }
            Require(exact.AllocatedBytes == 0);
            using (VideoFrame frame = await new PngImageDecoder().DecodeAsync(WithPng(simple, Metadata(6, true)),
                new() { PreferredWidth = 1 }, new(128)))
            { Require(frame.Format.Width == 1 && frame.Format.Height == 2); }
        }

        internal static async Task VerifyWebpAsync(string root)
        {
            foreach (string file in new[] { "webp-edge-1031-9-0.webp", "webp-lossy-alpha-75.webp", "webp-animation.webp" })
            {
                byte[] input = await File.ReadAllBytesAsync(Path.Combine(root, file));
                WebpImageDecoder decoder = new();
                MediaBufferAllocator allocator = new(16 * 1024 * 1024);
                List<byte[]> originals = [];
                List<VideoFrame> references = [];
                try
                {
                    await foreach (VideoFrame frame in decoder.DecodeFramesAsync(input, new(), allocator))
                    { references.Add(frame); originals.Add(frame.Data.ToArray()); }
                    foreach (bool little in new[] { false, true })
                    {
                        for (int orientation = 1; orientation <= 8; orientation++)
                        {
                            byte[] encoded = WithWebp(input, Metadata(orientation, little), references[0].Format.Width, references[0].Format.Height);
                            ImageInfo info = await decoder.ReadInfoAsync(encoded);
                            int index = 0;
                            List<VideoFrame> retained = [];
                            try
                            {
                                await foreach (VideoFrame frame in decoder.DecodeFramesAsync(encoded, new(), allocator))
                                {
                                    retained.Add(frame);
                                    Verify(frame, originals[index], references[index].Format.Width, references[index].Format.Height, orientation);
                                    Require(frame.Timestamp == references[index].Timestamp && frame.Duration == references[index].Duration);
                                    Require(frame.Format.Width == info.Width && frame.Format.Height == info.Height);
                                    index++;
                                }
                                Require(index == originals.Count);
                                for (int i = 0; i < index; i++)
                                { Verify(retained[i], originals[i], references[i].Format.Width, references[i].Format.Height, orientation); }
                            }
                            finally { foreach (VideoFrame frame in retained) { frame.Dispose(); } }
                            using VideoFrame still = await decoder.DecodeAsync(encoded, new(), allocator);
                            Verify(still, originals[0], references[0].Format.Width, references[0].Format.Height, orientation);
                            if (references.Count > 1)
                            {
                                using VideoFrame later = await decoder.DecodeAsync(encoded, new() { Time = references[1].Timestamp }, allocator);
                                Verify(later, originals[1], references[1].Format.Width, references[1].Format.Height, orientation);
                            }
                        }
                    }
                }
                finally { foreach (VideoFrame frame in references) { frame.Dispose(); } }
                Require(allocator.AllocatedBytes == 0);
            }
        }

        internal static byte[] WithWebp(byte[] encoded, byte[] metadata, int width, int height)
        {
            bool extended = encoded.AsSpan(12, 4).SequenceEqual("VP8X"u8);
            byte[] chunk = new byte[8 + metadata.Length + (metadata.Length & 1)];
            "EXIF"u8.CopyTo(chunk); BinaryPrimitives.WriteInt32LittleEndian(chunk.AsSpan(4), metadata.Length);
            metadata.CopyTo(chunk.AsSpan(8));
            byte[] result;
            if (extended) { result = [.. encoded, .. chunk]; result[20] |= 8; }
            else
            {
                byte[] canvas = new byte[18]; "VP8X"u8.CopyTo(canvas); canvas[4] = 10; canvas[8] = 8;
                Write24(canvas.AsSpan(12), width - 1); Write24(canvas.AsSpan(15), height - 1);
                result = [.. encoded.AsSpan(0, 12), .. canvas, .. encoded.AsSpan(12), .. chunk];
            }
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4), result.Length - 8);
            return result;
        }

        internal static void VerifyInvalidMetadata()
        {
            byte[] png = PngDecodeTestData.Image([0, 1, 2, 3, 4]);
            foreach (byte[] metadata in new[] { Array.Empty<byte>(), new byte[] { 1 }, Metadata(0, true), Metadata(9, false) })
            {
                MediaBufferAllocator allocator = new(128);
                bool rejected = false;
                try { using VideoFrame frame = new PngImageDecoder().Decode(WithPng(png, metadata), new(), allocator); }
                catch (InvalidDataException) { rejected = true; }
                Require(rejected && allocator.AllocatedBytes == 0);
            }
            foreach (int orientation in Enumerable.Range(1, 8))
            {
                OrthogonalTransform transform = ExifOrientation.ToTransform(orientation);
                for (int y = 0; y < 2; y++)
                {
                    for (int x = 0; x < 3; x++)
                    {
                        int outputWidth = transform.SwapsAxes ? 2 : 3, outputHeight = transform.SwapsAxes ? 3 : 2;
                        int dx = transform.Inverse.SourceX(x, y, outputWidth), dy = transform.Inverse.SourceY(x, y, outputHeight);
                        Require(transform.SourceX(dx, dy, 3) == x && transform.SourceY(dx, dy, 2) == y);
                    }
                }
            }
        }

        private static void Verify(VideoFrame frame, byte[] original, int width, int height, int orientation, int factor = 1)
        {
            int reducedWidth = (width - 1) / factor + 1, reducedHeight = (height - 1) / factor + 1;
            Require(frame.Format.Width == (orientation >= 5 ? reducedHeight : reducedWidth));
            Require(frame.Format.Height == (orientation >= 5 ? reducedWidth : reducedHeight));
            for (int y = 0; y < reducedHeight; y++)
            {
                for (int x = 0; x < reducedWidth; x++)
                {
                    (int dx, int dy) = orientation switch
                    {
                        1 => (x, y),
                        2 => (reducedWidth - 1 - x, y),
                        3 => (reducedWidth - 1 - x, reducedHeight - 1 - y),
                        4 => (x, reducedHeight - 1 - y),
                        5 => (y, x),
                        6 => (reducedHeight - 1 - y, x),
                        7 => (reducedHeight - 1 - y, reducedWidth - 1 - x),
                        8 => (y, reducedWidth - 1 - x),
                        _ => throw new ArgumentOutOfRangeException(nameof(orientation))
                    };
                    Require(frame.GetRow(0, dy).Slice(dx * 4, 4).SequenceEqual(original.AsSpan((y * factor * width + x * factor) * 4, 4)));
                }
            }
        }

        private static void Write24(Span<byte> target, int value)
        { target[0] = (byte)value; target[1] = (byte)(value >> 8); target[2] = (byte)(value >> 16); }
        private static void Require(bool condition)
        { if (!condition) { throw new InvalidDataException("Exif image orientation, timing, aspect ratio or ownership differs."); } }
    }
}
