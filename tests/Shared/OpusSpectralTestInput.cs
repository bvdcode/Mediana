// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Opus;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class OpusSpectralTestInput
    {
        public static int VerifyPartitions(string root) => Verify(root, "partitions.bin", "OPUSPRT1"u8, false);
        public static int VerifyBands(string root) => Verify(root, "spectral-bands.bin", "OPUSBND1"u8, true);

        private static int Verify(string root, string fileName, ReadOnlySpan<byte> header, bool bands)
        {
            MediaBufferAllocator allocator = new(128 * 1024);
            using CeltPulseCodebook codebook = CeltPulseCodebook.Create(176, 128, allocator);
            using BinaryReader reader = new(File.OpenRead(Path.Combine(root, fileName)));
            if (!reader.ReadBytes(8).AsSpan().SequenceEqual(header))
            {
                throw new InvalidDataException("The CELT spectral reference header is invalid.");
            }
            int cases = reader.ReadInt32();
            Span<float> vector = stackalloc float[176], low = stackalloc float[176], output = stackalloc float[176];
            for (int index = 0; index < cases; index++)
            {
                byte[] bytes = reader.ReadBytes(reader.ReadInt32());
                OpusRangeDecoder decoder = new(bytes);
                int band = reader.ReadInt32(), shift = reader.ReadInt32(), dimensions = reader.ReadInt32();
                int blocks = reader.ReadInt32(), change = 0;
                if (bands) { change = reader.ReadInt32(); }
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
                uint mask;
                if (bands)
                {
                    mask = CeltBandReader.Read(ref decoder, ref state, vector[..dimensions], budget, new(shift, blocks, change),
                        folding, output[..dimensions], gain, fill);
                }
                else
                {
                    mask = CeltPartitionReader.Read(ref decoder, ref state, vector[..dimensions], budget, blocks, folding, shift, gain, fill);
                }
                if (mask != reader.ReadUInt32() || state.RemainingEighths != reader.ReadInt32() || state.Seed != reader.ReadUInt32() ||
                    decoder.Range != reader.ReadUInt32() || decoder.BitsUsed != reader.ReadInt32() || decoder.EighthBitsUsed != reader.ReadInt32())
                {
                    throw new InvalidDataException($"CELT spectral {index} has a different mask, noise seed, allocation or entropy state.");
                }
                VerifyCoefficients(reader, vector[..dimensions], index, .000001f);
                if (bands) { VerifyCoefficients(reader, output[..dimensions], index, .00001f); }
            }
            if (reader.BaseStream.Position != reader.BaseStream.Length)
            {
                throw new InvalidDataException("The CELT spectral reference has trailing bytes.");
            }
            return cases;
        }

        private static void VerifyCoefficients(BinaryReader reader, ReadOnlySpan<float> coefficients, int index, float tolerance)
        {
            for (int at = 0; at < coefficients.Length; at++)
            {
                float expected = reader.ReadSingle();
                if (!float.IsFinite(coefficients[at]) || Math.Abs(coefficients[at] - expected) > tolerance)
                {
                    throw new InvalidDataException($"CELT spectral case {index}, coefficient {at}, differs: {coefficients[at]} / {expected}.");
                }
            }
        }
    }
}
