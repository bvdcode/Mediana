// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Opus;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class OpusVectorTestInput
    {
        public static int Verify(string root)
        {
            MediaBufferAllocator allocator = new(128 * 1024);
            using CeltPulseCodebook codebook = CeltPulseCodebook.Create(176, 128, allocator);
            using BinaryReader reader = new(File.OpenRead(Path.Combine(root, "normalized-vectors.bin")));
            if (!reader.ReadBytes(8).AsSpan().SequenceEqual("OPUSVEC1"u8))
            {
                throw new InvalidDataException("The normalized vector reference header is invalid.");
            }
            int cases = reader.ReadInt32();
            Span<float> vector = stackalloc float[176];
            for (int index = 0; index < cases; index++)
            {
                byte[] bytes = reader.ReadBytes(reader.ReadInt32());
                OpusRangeDecoder decoder = new(bytes);
                int dimensions = reader.ReadInt32(), pulses = reader.ReadInt32(), blocks = reader.ReadInt32();
                CeltSpreading spreading = (CeltSpreading)reader.ReadInt32();
                float gain = reader.ReadSingle();
                uint mask = CeltPulseVector.Read(codebook, ref decoder, vector[..dimensions], pulses, blocks, spreading, gain);
                if (mask != reader.ReadUInt32() || decoder.Range != reader.ReadUInt32() || decoder.BitsUsed != reader.ReadInt32() ||
                    decoder.EighthBitsUsed != reader.ReadInt32())
                {
                    throw new InvalidDataException($"Normalized vector case {index} has a different collapse mask or entropy state.");
                }
                for (int at = 0; at < dimensions; at++)
                {
                    float expected = reader.ReadSingle();
                    if (!float.IsFinite(vector[at]) || Math.Abs(vector[at] - expected) > 0.000001f)
                    {
                        throw new InvalidDataException($"Normalized vector case {index}, coefficient {at}, differs: {vector[at]} / {expected}.");
                    }
                }
            }
            if (reader.BaseStream.Position != reader.BaseStream.Length)
            {
                throw new InvalidDataException("The normalized vector reference has trailing bytes.");
            }
            return cases;
        }
    }
}
