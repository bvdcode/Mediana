// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Jpeg;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.Tests
{
    internal static class JpegCmykIccTestInput
    {
        internal static async Task VerifyAsync(string root, string raster)
        {
            foreach (string name in new[] { "cmyk-adobe", "cmyk-plain", "cmyk-progressive", "ycck-multiscan" })
            {
                byte[] original = await File.ReadAllBytesAsync(Path.Combine(raster, name + ".jpg"));
                byte[] cmyk = await File.ReadAllBytesAsync(Path.Combine(root, name + ".cmyk"));
                foreach (string variant in new[] { "v2-mft2-lab", "v4-mab2-xyz" })
                {
                    byte[] profile = await File.ReadAllBytesAsync(Path.Combine(root, variant + ".icc"));
                    byte[] reference = await File.ReadAllBytesAsync(Path.Combine(root, name + "-" + variant + ".rgba"));
                    byte[] encoded = Attach(original, profile);
                    MediaBufferAllocator allocator = new(1024 * 1024);
                    using (VideoFrame frame = await new JpegImageDecoder().DecodeAsync(encoded, new(), allocator))
                    {
                        encoded.AsSpan().Clear();
                        if (frame.Format.PixelFormat != VideoPixelFormat.Cmyk32 ||
                            frame.Format.ColorDescription is not IccColorDescription icc || !icc.Profile.Span.SequenceEqual(profile))
                        { throw new InvalidDataException("JPEG lost its owned CMYK samples or ICC profile."); }
                        Compare(cmyk, frame.Data.Span, 2, name);
                        using VideoFrame converted = SrgbConverter.ApplyWithColorPolicy(frame,
                            _ => throw new InvalidDataException("CMYK ICC conversion invoked the missing-color policy."), allocator);
                        Compare(reference, converted.Data.Span, 10, name + "/" + variant);
                        for (int i = 3; i < converted.Data.Length; i += 4)
                        {
                            if (converted.Data.Span[i] != 255) { throw new InvalidDataException("CMYK black channel became alpha."); }
                        }
                    }
                    if (allocator.AllocatedBytes != 0) { throw new InvalidDataException("CMYK JPEG retained frame storage."); }
                }
            }
        }

        internal static byte[] Attach(byte[] jpeg, ReadOnlySpan<byte> profile)
        {
            int split = profile.Length / 2;
            byte[] first = JpegTestData.Insert(jpeg, 226, [.. "ICC_PROFILE\0"u8, 1, 2, .. profile[..split]]);
            return JpegTestData.Insert(first, 226, [.. "ICC_PROFILE\0"u8, 2, 2, .. profile[split..]]);
        }

        private static void Compare(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual, int tolerance, string name)
        {
            if (expected.Length != actual.Length) { throw new InvalidDataException("CMYK JPEG reference dimensions differ."); }
            long total = 0;
            for (int i = 0; i < expected.Length; i++)
            {
                int error = Math.Abs(expected[i] - actual[i]);
                total += error;
                if (error > tolerance)
                { throw new InvalidDataException($"CMYK JPEG reference differs: {name}, byte {i}, expected {expected[i]}, actual {actual[i]}."); }
            }
            if ((double)total / expected.Length > 0.1) { throw new InvalidDataException("CMYK JPEG mean error exceeds 0.1: " + name); }
        }
    }
}
