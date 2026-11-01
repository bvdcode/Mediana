// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Security.Cryptography;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Images.Heif;

namespace Mediana.Tests
{
    internal static class HeifFrameOwnershipTestInput
    {
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
