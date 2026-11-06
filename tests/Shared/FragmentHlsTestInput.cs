// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Security.Cryptography;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;
using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    internal static class FragmentHlsTestInput
    {
        public static async Task VerifyAsync(string root)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using FileStream file = File.OpenRead(Path.Combine(root, "avc-segments.mp4"));
            await using RandomAccessStreamByteSource source = new(file, leaveOpen: true);
            await using (Mp4FragmentHlsSession session = await Mp4FragmentHlsSession.OpenAsync(source, allocator, 0, 1,
                new() { Planning = new() { TargetDuration = new(9, new TimeBase(1, 4)) } }))
            {
                if (session.Segments.Count != 3 || session.Streams.Count != 2)
                {
                    throw new InvalidDataException("Fragment HLS did not retain the expected indexed windows.");
                }
                using MemoryStream init = new();
                await session.WriteInitializationAsync(init);
                if (!init.GetBuffer().AsSpan(4, 4).SequenceEqual("ftyp"u8) || !init.CanWrite)
                {
                    throw new InvalidDataException("Fragment HLS initialization is missing or closed its destination.");
                }
                long retained = allocator.AllocatedBytes;
                byte[]? finalHash = null;
                foreach (int number in new[] { 2, 0, 1, 2 })
                {
                    using MemoryStream output = new();
                    await session.WriteSegmentAsync(number, output);
                    if (allocator.AllocatedBytes != retained || !output.GetBuffer().AsSpan(4, 4).SequenceEqual("moof"u8))
                    {
                        throw new InvalidDataException("An indexed fragment is missing or retained working storage.");
                    }
                    if (number == 2)
                    {
                        byte[] hash = SHA256.HashData(output.GetBuffer().AsSpan(0, checked((int)output.Length)));
                        if (finalHash is not null && !hash.AsSpan().SequenceEqual(finalHash))
                        {
                            throw new InvalidDataException("Repeated independent fragments differ.");
                        }
                        finalHash = hash;
                    }
                }
                if (!session.CreateVodPlaylist("init.mp4", number => $"{number}.m4s")
                    .Contains("#EXT-X-MAP:URI=\"init.mp4\"\n", StringComparison.Ordinal))
                {
                    throw new InvalidDataException("The fMP4 playlist is missing its initialization mapping.");
                }
            }
            if (allocator.AllocatedBytes != 0 || !file.CanRead)
            {
                throw new InvalidOperationException("The fragment session retained storage or closed its borrowed source.");
            }
        }
    }
}
