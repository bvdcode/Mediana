// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using Mediana.Codecs.Hevc;

namespace Mediana.Tests
{
    internal static class HevcWeightedPredictionTestInput
    {
        public static int Verify(string root, bool measureAllocations = false)
        {
            using GZipStream file = new(File.OpenRead(Path.Combine(root, "weights.bin.gz")), CompressionMode.Decompress);
            using BinaryReader input = new(file);
            if (!input.ReadBytes(8).AsSpan().SequenceEqual("HEVCWGT1"u8)) { throw new InvalidDataException("Invalid HEVC weight reference."); }
            int count = input.ReadInt32();
            for (int test = 0; test < count; test++)
            {
                int depth = input.ReadInt32(), denominator = input.ReadInt32(), weight0 = input.ReadInt32(), weight1 = input.ReadInt32();
                int offset0 = input.ReadInt32(), offset1 = input.ReadInt32(), bidirectional = input.ReadInt32(), length = input.ReadInt32();
                int[] first = new int[length], second = new int[length], expected = new int[length], output = new int[length];
                for (int i = 0; i < length; i++) { first[i] = input.ReadInt32(); second[i] = input.ReadInt32(); }
                for (int i = 0; i < length; i++) { expected[i] = input.ReadInt32(); }
                long before = GC.GetAllocatedBytesForCurrentThread();
                switch (bidirectional)
                {
                    case 0: HevcWeightedPrediction.Unidirectional(first, output, depth, denominator, weight0, offset0); break;
                    case 1: HevcWeightedPrediction.Bidirectional(first, second, output, depth, denominator, weight0, weight1, offset0, offset1); break;
                    default: throw new InvalidDataException("Invalid weight reference direction.");
                }
                if (measureAllocations && GC.GetAllocatedBytesForCurrentThread() != before)
                { throw new InvalidDataException("HEVC weight combination allocated working storage."); }
                for (int i = 0; i < length; i++)
                {
                    if (output[i] != expected[i])
                    { throw new InvalidDataException($"HEVC weight {test}/{i}: {output[i]}, expected {expected[i]}."); }
                }
            }
            if (file.ReadByte() != -1) { throw new InvalidDataException("The HEVC weight reference has trailing data."); }
            return count;
        }
    }
}
