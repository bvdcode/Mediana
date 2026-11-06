// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Security.Cryptography;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Images.Heif;
using Mediana.Processing;

namespace Mediana.Tests
{
    internal static class HeifFrameOwnershipTestInput
    {
        internal static async Task VerifyIccAsync(string root, string iccRoot)
        {
            foreach ((string name, string profileName, bool restricted) in new[]
            {
                ("basic-8bit", "sampled-v2", false), ("basic-8bit", "linear-v2", true),
                ("grid", "distinct-trc-p3-v4", false), ("alpha-rotated", "parametric-4-v4", true),
                ("identity", "gamma22-v2", false), ("monochrome", "gray-gamma22-v4", false)
            })
            {
                string kind = restricted ? "ricc" : "prof";
                byte[] input = await File.ReadAllBytesAsync(Path.Combine(root, $"icc-{kind}-{name}.heic"));
                byte[] profile = await File.ReadAllBytesAsync(Path.Combine(iccRoot, profileName + ".icc"));
                RgbaImage baseline = await HeicDocument.Load(await File.ReadAllBytesAsync(Path.Combine(root, name + ".heic"))).DecodePrimaryAsync();
                RgbaImage image = await HeicDocument.Load(input).DecodePrimaryAsync();
                if (!image.Pixels.Span.SequenceEqual(baseline.Pixels.Span) || !image.ColorInformation.IccProfile.Span.SequenceEqual(profile))
                {
                    throw new InvalidDataException("HEIF decoding must retain its RGB signal and owned ICC profile.");
                }
                MediaBufferAllocator allocator = new(image.Pixels.Length * 5);
                using (VideoFrame copied = image.ToVideoFrame(allocator, SampleAspectRatio.Square))
                using (VideoFrame direct = await new HeicImageDecoder(SampleAspectRatio.Square).DecodeAsync(input, new(), allocator))
                {
                    Array.Clear(input);
                    if (copied.Format.ColorDescription is not IccColorDescription copiedIcc ||
                        direct.Format.ColorDescription is not IccColorDescription directIcc ||
                        copiedIcc.Restricted != restricted || directIcc.Restricted != restricted ||
                        !directIcc.Profile.Span.SequenceEqual(profile) || !copiedIcc.Profile.Span.SequenceEqual(profile) ||
                        !copied.Data.Span.SequenceEqual(direct.Data.Span) || allocator.AllocatedBytes != image.Pixels.Length * 2)
                    {
                        throw new InvalidDataException("Copied and direct HEIF frames must retain independent pixels and the declared ICC profile.");
                    }
                    using VideoFrame converted = SrgbConverter.Apply(direct, allocator);
                    using VideoFrame copiedConverted = SrgbConverter.Apply(copied, allocator);
                    using VideoFrame fitted = SrgbPreview.Fit(direct, VideoDisplayMatrix.Identity, 16, 16, allocator);
                    if (!converted.Data.Span.SequenceEqual(copiedConverted.Data.Span) || converted.Format.ColorDescription is not null ||
                        converted.Format.Color != VideoColorInfo.Srgb || fitted.Format.Width > 16 || fitted.Format.Height > 16)
                    {
                        throw new InvalidDataException("HEIF frames must use common ICC conversion and fitted previews.");
                    }
                    for (int offset = 3; offset < image.Pixels.Length; offset += 4)
                    {
                        if (converted.Data.Span[offset] != image.Pixels.Span[offset])
                        {
                            throw new InvalidDataException("HEIF ICC conversion changed alpha.");
                        }
                    }
                }
                if (allocator.AllocatedBytes != 0)
                {
                    throw new InvalidDataException("HEIF ICC buffers were retained.");
                }
            }
        }

        public static async Task VerifyAsync(string root)
        {
            HeicImageDecoder decoder = new(new SampleAspectRatio(3, 2));
            foreach (string name in new[] { "basic-8bit", "alpha-rotated", "grid-alpha", "crop", "identity" })
            {
                byte[] input = await File.ReadAllBytesAsync(Path.Combine(root, name + ".heic"));
                RgbaImage expected = await HeicDocument.Load(input).DecodePrimaryAsync();
                byte[] hash = SHA256.HashData(expected.Pixels.Span);
                int bytes = expected.Pixels.Length;
                MediaBufferAllocator allocator = new(bytes * 2);
                using (VideoFrame first = await decoder.DecodeAsync(input, new(), allocator))
                {
                    using VideoFrame retained = first.Retain();
                    first.Dispose();
                    input.AsSpan().Clear();
                    for (int repeat = 0; repeat < 3; repeat++)
                    {
                        byte[] fresh = await File.ReadAllBytesAsync(Path.Combine(root, name + ".heic"));
                        using VideoFrame next = await decoder.DecodeAsync(fresh, new(), allocator);
                        fresh.AsSpan().Clear();
                        if (!SHA256.HashData(next.Data.Span).AsSpan().SequenceEqual(hash)
                            || !SHA256.HashData(retained.Data.Span).AsSpan().SequenceEqual(hash)
                            || next.Data.Equals(retained.Data) || allocator.AllocatedBytes != bytes * 2
                            || next.Format.SampleAspectRatio != new SampleAspectRatio(3, 2))
                        {
                            throw new InvalidDataException("HEIC frames must own independent pixels after input and workspace reuse.");
                        }
                    }
                }
                if (allocator.AllocatedBytes != 0)
                {
                    throw new InvalidDataException("HEIC frame disposal did not release its output budget.");
                }
            }
        }
    }
}
