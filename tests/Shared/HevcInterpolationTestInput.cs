// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using Mediana.Codecs.Hevc;

namespace Mediana.Tests
{
    internal static class HevcInterpolationTestInput
    {
        public static int Verify(string root, bool measureAllocations = false)
        {
            using GZipStream file = new(File.OpenRead(Path.Combine(root, "interpolation.bin.gz")), CompressionMode.Decompress);
            using BinaryReader input = new(file);
            if (!input.ReadBytes(8).AsSpan().SequenceEqual("HEVCINT1"u8)) { throw new InvalidDataException("Invalid HEVC interpolation reference."); }
            int cases = input.ReadInt32();
            for (int test = 0; test < cases; test++)
            {
                int depth = input.ReadInt32(); bool chroma = input.ReadInt32() != 0;
                int planeWidth = input.ReadInt32(), planeHeight = input.ReadInt32(), x = input.ReadInt32(), y = input.ReadInt32();
                int fractionX = input.ReadInt32(), fractionY = input.ReadInt32(), width = input.ReadInt32(), height = input.ReadInt32();
                ushort[] words = new ushort[checked(planeWidth * planeHeight)]; byte[] bytes = new byte[words.Length];
                for (int i = 0; i < words.Length; i++) { words[i] = input.ReadUInt16(); bytes[i] = (byte)words[i]; }
                int[] output = new int[checked(width * height)], expected = new int[output.Length];
                int[] working = new int[HevcInterpolation.GetWorkingSampleCount(width, height, chroma)];
                for (int i = 0; i < expected.Length; i++) { expected[i] = input.ReadInt32(); }
                long before = GC.GetAllocatedBytesForCurrentThread();
                HevcInterpolation.Predict<ushort, WordSampleFormat>(new(words, planeWidth, planeHeight), x, y,
                    fractionX, fractionY, chroma, depth, width, height, output, working);
                VerifyOutput(output, expected, before, measureAllocations, test);
                if (depth == 8)
                {
                    before = GC.GetAllocatedBytesForCurrentThread();
                    HevcInterpolation.Predict<byte, ByteSampleFormat>(new(bytes, planeWidth, planeHeight), x, y,
                        fractionX, fractionY, chroma, depth, width, height, output, working);
                    VerifyOutput(output, expected, before, measureAllocations, test);
                }
            }
            if (file.ReadByte() != -1) { throw new InvalidDataException("The HEVC interpolation reference has trailing data."); }
            return cases;
        }

        private static void VerifyOutput(ReadOnlySpan<int> actual, ReadOnlySpan<int> expected, long before, bool measure, int test)
        {
            if (measure && GC.GetAllocatedBytesForCurrentThread() != before) { throw new InvalidDataException("HEVC interpolation allocated working storage."); }
            for (int i = 0; i < actual.Length; i++)
            {
                if (actual[i] != expected[i]) { throw new InvalidDataException($"HEVC interpolation {test}/{i}: {actual[i]}, expected {expected[i]}."); }
            }
        }
    }
}
