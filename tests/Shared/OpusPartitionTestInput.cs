// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Opus;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class OpusPartitionTestInput
    {
        public static int Verify(string root)
        {
            MediaBufferAllocator allocator = new(128 * 1024);
            using CeltPulseCodebook codebook = CeltPulseCodebook.Create(176, 128, allocator);
            using BinaryReader reader = new(File.OpenRead(Path.Combine(root, "partitions.bin")));
            if (!reader.ReadBytes(8).AsSpan().SequenceEqual("OPUSPRT1"u8))
            {
                throw new InvalidDataException("The CELT partition reference header is invalid.");
            }
            int cases = reader.ReadInt32();
            Span<float> vector = stackalloc float[176], low = stackalloc float[176];
            for (int index = 0; index < cases; index++)
            {
                byte[] bytes = reader.ReadBytes(reader.ReadInt32());
                OpusRangeDecoder decoder = new(bytes);
                int band = reader.ReadInt32(), shift = reader.ReadInt32(), dimensions = reader.ReadInt32();
                int blocks = reader.ReadInt32();
                CeltSpreading spread = (CeltSpreading)reader.ReadInt32();
                int budget = reader.ReadInt32(), remaining = reader.ReadInt32();
                uint fill = reader.ReadUInt32(), seed = reader.ReadUInt32();
                float gain = reader.ReadSingle();
                bool hasLow = reader.ReadInt32() != 0;
                if (hasLow)
                {
                    for (int at = 0; at < dimensions; at++) { low[at] = reader.ReadSingle(); }
                }
                scoped ReadOnlySpan<float> folding = default;
                if (hasLow) { folding = low[..dimensions]; }
                CeltBandDecoderState state = new(codebook, band, spread, remaining, seed);
                uint mask = CeltPartitionReader.Read(ref decoder, ref state, vector[..dimensions], budget, blocks, folding, shift, gain, fill);
                if (mask != reader.ReadUInt32() || state.RemainingEighths != reader.ReadInt32() || state.Seed != reader.ReadUInt32() ||
                    decoder.Range != reader.ReadUInt32() || decoder.BitsUsed != reader.ReadInt32() || decoder.EighthBitsUsed != reader.ReadInt32())
                {
                    throw new InvalidDataException($"CELT partition {index} has a different mask, noise seed, allocation or entropy state.");
                }
                for (int at = 0; at < dimensions; at++)
                {
                    float expected = reader.ReadSingle();
                    if (!float.IsFinite(vector[at]) || Math.Abs(vector[at] - expected) > 0.000001f)
                    {
                        throw new InvalidDataException($"CELT partition {index}, coefficient {at}, differs: {vector[at]} / {expected}.");
                    }
                }
            }
            if (reader.BaseStream.Position != reader.BaseStream.Length)
            {
                throw new InvalidDataException("The CELT partition reference has trailing bytes.");
            }
            return cases;
        }
    }
}
