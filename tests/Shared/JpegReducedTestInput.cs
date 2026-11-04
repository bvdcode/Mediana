// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using Mediana.Codecs.Jpeg;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class JpegReducedTestInput
    {
        internal static async Task VerifyReferencesAsync(string root)
        {
            foreach (string line in await File.ReadAllLinesAsync(Path.Combine(root, "reduced.tsv")))
            {
                string[] fields = line.Split('\t');
                string name = fields[0];
                int reduction = int.Parse(fields[1], CultureInfo.InvariantCulture);
                int width = int.Parse(fields[2], CultureInfo.InvariantCulture);
                int height = int.Parse(fields[3], CultureInfo.InvariantCulture);
                byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(root, name + ".jpg"));
                byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, name + ".reduce" + fields[1] + ".rgba"));
                MediaBufferAllocator allocator = new(1024 * 1024);
                using (JpegImage image = JpegDecoder.DecodeReducedComponents(encoded, allocator, reduction))
                {
                    Require(image.EncodedWidth == width && image.EncodedHeight == height
                        && image.Width == (width + reduction - 1) / reduction && image.Height == (height + reduction - 1) / reduction);
                    byte[] actual = new byte[expected.Length + 3];
                    actual.AsSpan().Fill(19);
                    JpegColorSpace color = image.AdobeColorTransform switch
                    {
                        0 => JpegColorSpace.Rgb,
                        null or 1 => JpegColorSpace.YCbCr,
                        _ => throw new InvalidDataException("Invalid reduced JPEG reference transform.")
                    };
                    image.WriteRgbaTo(actual, color);
                    Require(expected.Length == image.Width * image.Height * 4 && actual.AsSpan(expected.Length).IndexOfAnyExcept((byte)19) < 0);
                    for (int i = 0; i < expected.Length; i++)
                    {
                        if (Math.Abs(actual[i] - expected[i]) > ((i & 3) == 3 ? 0 : 3))
                        {
                            throw new InvalidDataException($"Reduced JPEG reference differs: {name}, 1/{reduction}, byte {i}: {actual[i]} versus {expected[i]}.");
                        }
                    }
                }
                Require(allocator.AllocatedBytes == 0);
            }
        }

        internal static void VerifyBudgets()
        {
            byte[] encoded = JpegTestData.Image();
            foreach (int reduction in new[] { 1, 2, 4, 8 })
            {
                int bytes = 64 / (reduction * reduction);
                MediaBufferAllocator allocator = new(bytes);
                using (JpegImage image = JpegDecoder.DecodeReducedComponents(encoded, allocator, reduction))
                {
                    Require(allocator.AllocatedBytes == bytes && image.Width == 1 && image.Height == 1);
                    byte[] actual = new byte[4];
                    image.WriteRgbaTo(actual, JpegColorSpace.Rgb);
                    Require(actual.AsSpan().SequenceEqual(new byte[] { 128, 128, 128, 255 }));
                }
                Require(allocator.AllocatedBytes == 0);
            }
        }

        internal static async Task VerifySamplingAsync(string root)
        {
            foreach (string line in await File.ReadAllLinesAsync(Path.Combine(root, "corpus.tsv")))
            {
                string[] fields = line.Split('\t');
                int width = int.Parse(fields[1], CultureInfo.InvariantCulture);
                int height = int.Parse(fields[2], CultureInfo.InvariantCulture);
                byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(root, fields[0] + ".jpg"));
                foreach (int reduction in new[] { 2, 4, 8 })
                {
                    MediaBufferAllocator allocator = new(1024 * 1024);
                    using (JpegImage image = JpegDecoder.DecodeReducedComponents(encoded, allocator, reduction))
                    {
                        Require(image.Width == (width + reduction - 1) / reduction && image.Height == (height + reduction - 1) / reduction);
                        byte[] rgba = new byte[image.Width * image.Height * 4];
                        image.WriteRgbaTo(rgba, JpegColorSpace.YCbCr);
                        for (int i = 3; i < rgba.Length; i += 4) { Require(rgba[i] == 255); }
                    }
                    Require(allocator.AllocatedBytes == 0);
                }
            }
        }

        internal static async Task VerifyFailuresAsync(string root)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(root, "rgb-s0-35x19.jpg"));
            Reject<InvalidDataException>(() => { using JpegImage image = JpegDecoder.DecodeReducedComponents(encoded, allocator, 8, new() { MaximumPixels = 1 }); });
            Reject<ArgumentOutOfRangeException>(() => { using JpegImage image = JpegDecoder.DecodeReducedComponents(encoded, allocator, 3); });
            Reject<OperationCanceledException>(() => { using JpegImage image = JpegDecoder.DecodeReducedComponents(encoded, allocator, 2, cancellationToken: new(true)); });
            byte[] minimal = JpegTestData.Image();
            for (int i = 0; i < minimal.Length; i++)
            {
                int length = i;
                Reject<InvalidDataException>(() => { using JpegImage image = JpegDecoder.DecodeReducedComponents(minimal.AsMemory(0, length), allocator, 4); });
                Require(allocator.AllocatedBytes == 0);
            }
            MediaBufferAllocator tiny = new(3);
            Reject<MediaMemoryLimitException>(() => { using JpegImage image = JpegDecoder.DecodeReducedComponents(minimal, tiny, 4); });
            Require(tiny.AllocatedBytes == 0);
            byte[] progressive = await File.ReadAllBytesAsync(Path.Combine(root, "progressive-l-s0-35x19.jpg"));
            MediaBufferAllocator coefficients = new(1024);
            Reject<MediaMemoryLimitException>(() => { using JpegImage image = JpegDecoder.DecodeReducedComponents(progressive, coefficients, 8); });
            Require(coefficients.AllocatedBytes == 0 && allocator.AllocatedBytes == 0);
        }

        private static void Reject<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidDataException("Reduced JPEG accepted an invalid input or budget.");
        }

        private static void Require(bool condition)
        {
            if (!condition) { throw new InvalidDataException("Reduced JPEG dimensions, pixels or component lifetime differ."); }
        }
    }
}
