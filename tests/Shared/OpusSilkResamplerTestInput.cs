// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Opus;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class OpusSilkResamplerTestInput
    {
        public static int Verify(string root)
        {
            using BinaryReader reader = new(File.OpenRead(Path.Combine(root, "silk-resampler.bin")));
            if (!reader.ReadBytes(8).AsSpan().SequenceEqual("OPUSRSF1"u8))
            {
                throw new InvalidDataException("The SILK resampling reference header is invalid.");
            }
            MediaBufferAllocator allocator = new(1024);
            Span<short> input = stackalloc short[320], output = stackalloc short[961];
            SilkResampler? resampler = null;
            int cases = reader.ReadInt32();
            try
            {
                for (int index = 0; index < cases; index++)
                {
                    int rate = reader.ReadInt32(), frame = reader.ReadInt32(), count = reader.ReadInt32();
                    if (frame == 0)
                    {
                        resampler?.Dispose();
                        if (allocator.AllocatedBytes != 0) { throw new InvalidDataException("The SILK resampler retained its working allocation."); }
                        resampler = new(rate, allocator);
                    }
                    for (int at = 0; at < count; at++) { input[at] = reader.ReadInt16(); }
                    output.Fill(short.MinValue);
                    int written = resampler!.Process(input[..count], output), expectedCount = count * 48000 / rate;
                    if (written != expectedCount) { throw new InvalidDataException($"SILK resampling case {index} has a different output length."); }
                    for (int at = 0; at < written; at++)
                    {
                        short expected = reader.ReadInt16();
                        if (output[at] != expected)
                        {
                            throw new InvalidDataException($"SILK resampling case {index}, sample {at}, differs: {output[at]} / {expected}.");
                        }
                    }
                    foreach (short value in output[written..])
                    {
                        if (value != short.MinValue) { throw new InvalidDataException("SILK resampling overwrote unused output storage."); }
                    }
                }
            }
            finally { resampler?.Dispose(); }
            if (reader.BaseStream.Position != reader.BaseStream.Length) { throw new InvalidDataException("The SILK resampling reference has trailing bytes."); }
            if (allocator.AllocatedBytes != 0) { throw new InvalidDataException("The SILK resampler retained its working allocation."); }
            return cases;
        }
    }
}
