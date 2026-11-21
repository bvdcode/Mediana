// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.Tests
{
    internal static class IccCmykTestInput
    {
        internal static async Task VerifyAsync(string root)
        {
            byte[] samples = await File.ReadAllBytesAsync(Path.Combine(root, "samples.cmyk"));
            foreach (string path in Directory.EnumerateFiles(root, "*.icc").Order(StringComparer.Ordinal))
            {
                byte[] profile = await File.ReadAllBytesAsync(path);
                byte[] expected = await File.ReadAllBytesAsync(Path.ChangeExtension(path, ".rgba"));
                MediaBufferAllocator allocator = new(1024 * 1024);
                using (VideoFrame input = CreateFrame(profile, samples, 64, 64, allocator))
                using (VideoFrame converted = SrgbConverter.ApplyWithColorPolicy(input,
                    _ => throw new InvalidDataException("Color policy replaced the CMYK ICC transform."), allocator))
                using (VideoFrame fitted = SrgbPreview.Fit(input, VideoDisplayMatrix.Identity, 23, 23, allocator))
                using (VideoFrame expectedFit = DisplayResize.Fit(converted, 23, 23, allocator))
                {
                    if (expected.Length != converted.Data.Length) { throw new InvalidDataException("CMYK reference size differs."); }
                    for (int index = 0; index < expected.Length; index++)
                    {
                        if (Math.Abs(expected[index] - converted.Data.Span[index]) > ((index & 3) == 3 ? 0 : 2))
                        {
                            throw new InvalidDataException($"CMYK ICC differs from LittleCMS: {Path.GetFileName(path)}, byte {index}, expected {expected[index]}, actual {converted.Data.Span[index]}.");
                        }
                    }
                    if (!fitted.Data.Span.SequenceEqual(expectedFit.Data.Span) || converted.Format.ColorDescription is not null ||
                        converted.Format.Color != VideoColorInfo.Srgb || converted.Format.PixelFormat != VideoPixelFormat.Rgba32)
                    { throw new InvalidDataException("CMYK ICC preview sampling or output color labels differ."); }
                }
                if (allocator.AllocatedBytes != 0) { throw new InvalidDataException("CMYK ICC conversion retained frame storage."); }
            }
        }

        internal static VideoFrame CreateFrame(ReadOnlySpan<byte> profile, ReadOnlySpan<byte> pixels, int width, int height,
            MediaBufferAllocator allocator)
        {
            VideoFormat format = new(width, height, VideoPixelFormat.Cmyk32, new(Range: ColorRange.Full),
                SampleAspectRatio.Square, new IccColorDescription(profile, false));
            PlaneLayout[] planes = format.CreateTightLayout();
            using WritableMediaBuffer output = allocator.Allocate(checked((int)planes[0].EndOffset));
            pixels.CopyTo(output.Span);
            using MediaBuffer storage = output.Publish();
            return new(format, storage, planes);
        }
    }
}
