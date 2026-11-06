// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text.Json;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.Tests
{
    internal static class IccColorTestInput
    {
        internal static async Task VerifyAsync(string root)
        {
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "manifest.json")));
            foreach (JsonElement entry in manifest.RootElement.EnumerateArray())
            {
                string name = entry.GetProperty("name").GetString()!;
                byte[] profile = await File.ReadAllBytesAsync(Path.Combine(root, name + ".icc"));
                byte[] pixels = await File.ReadAllBytesAsync(Path.Combine(root, name + ".input.rgba"));
                byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, name + ".expected.rgba"));
                MediaBufferAllocator allocator = new(1024 * 1024);
                using (VideoFrame input = CreateFrame(profile, pixels, entry.GetProperty("width").GetInt32(), entry.GetProperty("height").GetInt32(), allocator))
                using (VideoFrame converted = SrgbConverter.ApplyWithColorPolicy(input,
                    _ => throw new InvalidDataException("Color-label policy replaced the ICC profile."), allocator))
                using (VideoFrame fitted = SrgbPreview.Fit(input, VideoDisplayMatrix.Identity, 64, 64, allocator))
                using (VideoFrame expectedFit = DisplayResize.Fit(converted, 64, 64, allocator))
                {
                    int tolerance = entry.GetProperty("maximum_error").GetInt32();
                    for (int index = 0; index < expected.Length; index++)
                    {
                        if (Math.Abs(expected[index] - converted.Data.Span[index]) > (index % 4 == 3 ? 0 : tolerance))
                        {
                            throw new InvalidDataException($"ICC pixels differ from LittleCMS: {name}, byte {index}, expected {expected[index]}, actual {converted.Data.Span[index]}.");
                        }
                    }
                    if (!fitted.Data.Span.SequenceEqual(expectedFit.Data.Span) || converted.Format.ColorDescription is not null ||
                        converted.Format.Color != VideoColorInfo.Srgb)
                    {
                        throw new InvalidDataException("ICC conversion, preview sampling or output color labels differ: " + name);
                    }
                }
                if (allocator.AllocatedBytes != 0) { throw new InvalidDataException("ICC conversion retained frame storage: " + name); }
            }
        }

        internal static VideoFrame CreateFrame(ReadOnlySpan<byte> profile, ReadOnlySpan<byte> pixels, int width, int height,
            MediaBufferAllocator allocator)
        {
            VideoFormat format = new(width, height, VideoPixelFormat.Rgba32, VideoColorInfo.Srgb,
                SampleAspectRatio.Square, new IccColorDescription(profile, false));
            PlaneLayout[] planes = format.CreateTightLayout();
            using WritableMediaBuffer output = allocator.Allocate(checked((int)planes[0].EndOffset));
            pixels.CopyTo(output.Span);
            using MediaBuffer storage = output.Publish();
            return new(format, storage, planes);
        }
    }
}
