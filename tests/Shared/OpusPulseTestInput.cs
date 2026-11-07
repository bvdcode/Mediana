// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Opus;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class OpusPulseTestInput
    {
        public static int Verify(string root)
        {
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            using (CeltPulseCodebook codebook = CeltPulseCodebook.Create(256, 2048, allocator))
            using (BinaryReader reader = new(File.OpenRead(Path.Combine(root, "pulse-vectors.bin"))))
            {
                if (!reader.ReadBytes(8).AsSpan().SequenceEqual("OPUSPVQ1"u8))
                {
                    throw new InvalidDataException("The pulse reference header is invalid.");
                }
                int cases = reader.ReadInt32();
                Span<int> vector = stackalloc int[256];
                for (int index = 0; index < cases; index++)
                {
                    int dimensions = reader.ReadInt32(), pulses = reader.ReadInt32();
                    uint count = reader.ReadUInt32(), rank = reader.ReadUInt32();
                    if (!codebook.TryGetSize(dimensions, pulses, out uint size) || size != count)
                    {
                        throw new InvalidDataException("The pulse codebook size differs from the reference.");
                    }
                    ulong energy = codebook.DecodeRank(rank, vector[..dimensions], pulses);
                    ulong expectedEnergy = 0;
                    for (int at = 0; at < dimensions; at++)
                    {
                        int expected = reader.ReadInt32();
                        if (vector[at] != expected)
                        {
                            throw new InvalidDataException($"Pulse vector {index}, coefficient {at}, differs from the reference.");
                        }
                        expectedEnergy += (ulong)((long)expected * expected);
                    }
                    if (energy != expectedEnergy)
                    {
                        throw new InvalidDataException("The pulse energy differs from the reference.");
                    }
                }
                if (reader.BaseStream.Position != reader.BaseStream.Length)
                {
                    throw new InvalidDataException("The pulse reference has trailing bytes.");
                }
                return cases;
            }
        }
    }
}
