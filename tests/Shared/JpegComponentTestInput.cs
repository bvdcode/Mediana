// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Jpeg;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class JpegComponentTestInput
    {
        internal static async Task VerifyCorpusAsync(string root)
        {
            foreach (string line in await File.ReadAllLinesAsync(Path.Combine(root, "corpus.tsv")))
            {
                string name = line.Split('\t')[0];
                byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(root, name + ".jpg"));
                byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, name + ".rgba"));
                MediaBufferAllocator allocator = new(1024 * 1024);
                using (JpegImage image = JpegDecoder.DecodeComponents(encoded, allocator))
                {
                    encoded.AsSpan().Clear();
                    byte[] rgba = new byte[expected.Length + 3];
                    rgba.AsSpan().Fill(27);
                    JpegColorSpace color = image.AdobeColorTransform switch
                    {
                        0 => JpegColorSpace.Rgb,
                        null or 1 => JpegColorSpace.YCbCr,
                        2 => throw new NotSupportedException("The reference corpus contains an unsupported YCCK transform."),
                        _ => throw new InvalidDataException("The reference corpus contains an invalid Adobe transform.")
                    };
                    image.WriteRgbaTo(rgba, color);
                    Require(rgba.Length - 3 == image.Width * image.Height * 4 && rgba.AsSpan(expected.Length).IndexOfAnyExcept((byte)27) < 0);
                    for (int i = 0; i < expected.Length; i++)
                    {
                        Require(Math.Abs(rgba[i] - expected[i]) <= ((i & 3) == 3 ? 0 : 4));
                    }
                }
                Require(allocator.AllocatedBytes == 0);
            }
        }

        internal static void VerifyOwnership()
        {
            MediaBufferAllocator allocator = new(64);
            using JpegImage image = JpegDecoder.DecodeComponents(JpegTestData.Image(), allocator);
            Require(allocator.AllocatedBytes == 64 && image.Width == 1 && image.Height == 1 && image.ComponentCount == 1 && image.AdobeColorTransform is null);
            byte[] output = new byte[7];
            image.WriteRgbaTo(output, JpegColorSpace.Rgb);
            Require(output.AsSpan(0, 4).SequenceEqual(new byte[] { 128, 128, 128, 255 }) && allocator.AllocatedBytes == 64);
            image.WriteRgbaTo(output, JpegColorSpace.YCbCr);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                image.WriteRgbaTo(output, JpegColorSpace.YCbCr);
            }
            Require(GC.GetAllocatedBytesForCurrentThread() == before);
            Reject<ArgumentException>(() => image.WriteRgbaTo(output.AsSpan(0, 3), JpegColorSpace.Rgb));
            Reject<ArgumentOutOfRangeException>(() => image.WriteRgbaTo(output, (JpegColorSpace)99));
            using CancellationTokenSource cancelled = new();
            cancelled.Cancel();
            output.AsSpan().Fill(17);
            Reject<OperationCanceledException>(() => image.WriteRgbaTo(output, JpegColorSpace.Rgb, cancelled.Token));
            Require(output.All(value => value == 17));
            image.Dispose();
            image.Dispose();
            Require(allocator.AllocatedBytes == 0);
            Reject<ObjectDisposedException>(() => image.WriteRgbaTo(output, JpegColorSpace.Rgb));
        }

        internal static void VerifyMetadata()
        {
            byte[] encoded = JpegTestData.Insert(JpegTestData.Image(), 225, "Exif\0\0arbitrary embedded metadata"u8);
            encoded = JpegTestData.Insert(encoded, 226, "ICC_PROFILE\0ignored presentation"u8);
            encoded = JpegTestData.Insert(encoded, 224, "JFIF\0ignored presentation"u8);
            encoded = JpegTestData.Insert(encoded, 238, [.. "Adobe"u8, 0, 100, 0, 0, 0, 0, 1]);
            MediaBufferAllocator allocator = new(64);
            using (JpegImage image = JpegDecoder.DecodeComponents(encoded, allocator))
            {
                Require(image.AdobeColorTransform == 1 && image.ComponentCount == 1);
            }
            Require(allocator.AllocatedBytes == 0);
            Reject<InvalidDataException>(() => { using JpegImage invalid = JpegDecoder.DecodeComponents(JpegTestData.Insert(encoded, 238, [.. "Adobe"u8, 0, 100, 0, 0, 0, 0, 0]), allocator); });
            Require(allocator.AllocatedBytes == 0);
        }

        internal static void VerifyFailures()
        {
            byte[] valid = JpegTestData.Image();
            MediaBufferAllocator allocator = new(64);
            for (int length = 0; length < valid.Length; length++)
            {
                int extent = length;
                Reject<InvalidDataException>(() => { using JpegImage invalid = JpegDecoder.DecodeComponents(valid.AsMemory(0, extent), allocator); });
                Require(allocator.AllocatedBytes == 0);
            }
            MediaBufferAllocator tooSmall = new(63);
            Reject<MediaMemoryLimitException>(() => { using JpegImage invalid = JpegDecoder.DecodeComponents(valid, tooSmall); });
            Require(tooSmall.AllocatedBytes == 0);
            using CancellationTokenSource cancelled = new();
            cancelled.Cancel();
            Reject<OperationCanceledException>(() => { using JpegImage invalid = JpegDecoder.DecodeComponents(valid, allocator, cancellationToken: cancelled.Token); });
            Require(allocator.AllocatedBytes == 0);
        }

        private static void Reject<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidDataException("The JPEG component operation accepted invalid data or ownership.");
        }

        private static void Require(bool condition)
        {
            if (!condition) { throw new InvalidDataException("JPEG component bytes, lifetime, destination or budget differs."); }
        }
    }
}
