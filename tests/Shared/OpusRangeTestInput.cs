// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Opus;

namespace Mediana.Tests
{
    internal static class OpusRangeTestInput
    {
        public static int Verify(string root)
        {
            using BinaryReader input = new(File.OpenRead(Path.Combine(root, "range-decoder.bin")));
            if (!input.ReadBytes(8).AsSpan().SequenceEqual("OPUSRNG1"u8)) { throw new InvalidDataException("The entropy reference header is invalid."); }
            int cases = input.ReadInt32();
            int operations = 0;
            for (int test = 0; test < cases; test++)
            {
                byte[] bytes = input.ReadBytes(input.ReadInt32());
                OpusRangeDecoder decoder = new(bytes);
                int count = input.ReadInt32();
                if (input.ReadUInt32() != decoder.Range || input.ReadInt32() != decoder.BitsUsed || input.ReadInt32() != decoder.EighthBitsUsed)
                {
                    throw new InvalidDataException("The range decoder initialization differs from its reference.");
                }
                for (int step = 0; step < count; step++)
                {
                    int kind = input.ReadByte();
                    uint parameter = input.ReadUInt32(), expected = input.ReadUInt32(), range = input.ReadUInt32();
                    int bits = input.ReadInt32(), eighths = input.ReadInt32(), error = input.ReadInt32();
                    if (error != 0)
                    {
                        if (error != 1 || kind != 2 || step != count - 1)
                        {
                            throw new InvalidDataException("A corrupt entropy reference must terminate at an out-of-range uniform integer.");
                        }
                        continue;
                    }
                    uint result = Read(ref decoder, kind, parameter);
                    if (result != expected || range != decoder.Range || bits != decoder.BitsUsed || eighths != decoder.EighthBitsUsed)
                    {
                        throw new InvalidDataException($"Entropy case {test}, step {step}, operation {kind}({parameter}): symbol {result}/{expected}; range {decoder.Range}/{range}; bits {decoder.BitsUsed}/{bits}; eighths {decoder.EighthBitsUsed}/{eighths}.");
                    }
                    operations++;
                }
            }
            if (cases != 128 || input.BaseStream.Position != input.BaseStream.Length)
            {
                throw new InvalidDataException("The entropy reference count or consumed size is inconsistent.");
            }
            return operations;
        }

        private static uint Read(ref OpusRangeDecoder decoder, int kind, uint parameter) => kind switch
        {
            0 => decoder.ReadBit((int)parameter) ? 1u : 0u,
            1 => parameter switch
            {
                0 => (uint)decoder.ReadInverseCdf([255, 200, 83, 11, 0]),
                1 => (uint)decoder.ReadInverseCdf([128, 0]),
                2 => (uint)decoder.ReadInverseCdf([7, 5, 4, 0], 3),
                3 => (uint)decoder.ReadInverseCdf([0], 1),
                _ => throw new InvalidDataException("An inverse cumulative reference model is absent.")
            },
            2 => decoder.ReadUniform(parameter),
            3 => decoder.ReadRawBits((int)parameter),
            4 => parameter switch
            {
                0 => (uint)decoder.ReadFrequencies([0, 2, 7, 13, 1, 0, 8]),
                1 => (uint)decoder.ReadFrequencies([4, 4, 4, 4]),
                2 => (uint)decoder.ReadFrequencies([1, 1000, 16384, 73, 40000]),
                3 => (uint)decoder.ReadFrequencies([65535]),
                _ => throw new InvalidDataException("A frequency reference model is absent.")
            },
            _ => throw new InvalidDataException("An entropy reference operation is absent.")
        };
    }
}
