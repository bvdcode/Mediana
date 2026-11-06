// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Opus;

namespace Mediana.Tests
{
    internal static class OpusLaplaceTestInput
    {
        public static int Verify(string root)
        {
            using BinaryReader reader = new(File.OpenRead(Path.Combine(root, "laplace-decoder.bin")));
            if (!reader.ReadBytes(8).AsSpan().SequenceEqual("OPUSLAP1"u8))
            {
                throw new InvalidDataException("The Laplace reference header is invalid.");
            }
            int cases = reader.ReadInt32(), operations = 0;
            for (int index = 0; index < cases; index++)
            {
                byte[] data = reader.ReadBytes(reader.ReadInt32());
                OpusRangeDecoder decoder = new(data);
                int count = reader.ReadInt32();
                for (int at = 0; at < count; at++)
                {
                    int zero = reader.ReadInt32(), decay = reader.ReadInt32();
                    int value = decoder.ReadLaplace(zero, decay);
                    if (value != reader.ReadInt32() || decoder.Range != reader.ReadUInt32() ||
                        decoder.BitsUsed != reader.ReadInt32() || decoder.EighthBitsUsed != reader.ReadInt32())
                    {
                        throw new InvalidDataException($"Laplace reference case {index}, operation {at}, differs.");
                    }
                    operations++;
                }
            }
            if (reader.BaseStream.Position != reader.BaseStream.Length)
            {
                throw new InvalidDataException("The Laplace reference has trailing bytes.");
            }
            return operations;
        }
    }
}
