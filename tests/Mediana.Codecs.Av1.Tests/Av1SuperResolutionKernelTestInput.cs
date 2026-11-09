// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Mediana.Codecs.Av1.Tests
{
    internal static class Av1SuperResolutionKernelTestInput
    {
        internal static void Verify(string root)
        {
            using FileStream input = File.OpenRead(Path.Combine(root, "super-resolution-rows.gz"));
            using GZipStream decompressed = new(input, CompressionMode.Decompress);
            using BinaryReader reader = new(decompressed);
            Require(reader.ReadInt32() == 0x32525341);
            int count = reader.ReadInt32();
            long samples = 0;
            for (int test = 0; test < count; test++)
            {
                int index = reader.ReadInt32(), coded = reader.ReadInt32(), upscaled = reader.ReadInt32();
                int padded = reader.ReadInt32(), depth = reader.ReadInt32();
                Require(index == test);
                int maximum = (1 << depth) - 1;
                byte[] expected = reader.ReadBytes(SHA256.HashSizeInBytes);
                byte[] actual = new byte[upscaled * (depth == 8 ? 1 : 2)];
                if (depth == 8)
                {
                    byte[] source = new byte[padded];
                    for (int column = 0; column < padded; column++) { source[column] = (byte)Sample(column, test, maximum); }
                    Av1SuperResolutionFilter.Row(source, coded, actual);
                }
                else
                {
                    ushort[] source = new ushort[padded], output = new ushort[upscaled];
                    for (int column = 0; column < padded; column++) { source[column] = (ushort)Sample(column, test, maximum); }
                    Av1SuperResolutionFilter.Row(source, coded, output, depth);
                    for (int column = 0; column < upscaled; column++) { BinaryPrimitives.WriteUInt16LittleEndian(actual.AsSpan(column * 2), output[column]); }
                }
                if (!SHA256.HashData(actual).AsSpan().SequenceEqual(expected))
                {
                    throw new InvalidDataException($"Super-resolution differs from the independent reference at case {index}.");
                }
                samples += upscaled;
            }
            Require(decompressed.ReadByte() == -1 && count == 720 && samples == 2573376);
        }

        private static int Sample(int column, int test, int maximum) =>
            (int)(((column * 53L + test * 17L) ^ (column * (long)column * 7 + test * 13L)) & maximum);

        private static void Require(bool condition)
        {
            if (!condition) { throw new InvalidDataException("The super-resolution reference is invalid."); }
        }
    }
}
