// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Opus;

namespace Mediana.Tests
{
    internal static class OpusEnergyTestInput
    {
        public static int Verify(string root)
        {
            using BinaryReader reader = new(File.OpenRead(Path.Combine(root, "energy-decoder.bin")));
            if (!reader.ReadBytes(8).AsSpan().SequenceEqual("OPUSENE1"u8))
            {
                throw new InvalidDataException("The energy reference header is invalid.");
            }
            int cases = reader.ReadInt32();
            Span<float> energies = stackalloc float[42];
            Span<int> fine = stackalloc int[21];
            Span<byte> priority = stackalloc byte[21];
            for (int index = 0; index < cases; index++)
            {
                byte[] data = reader.ReadBytes(reader.ReadInt32());
                OpusRangeDecoder decoder = new(data);
                int channels = reader.ReadInt32(), start = reader.ReadInt32(), end = reader.ReadInt32();
                int shift = reader.ReadInt32();
                bool intra = reader.ReadInt32() != 0;
                Span<float> active = energies[..(channels * 21)];
                for (int at = 0; at < active.Length; at++) { active[at] = reader.ReadSingle(); }
                for (int at = 0; at < fine.Length; at++) { fine[at] = reader.ReadInt32(); }
                for (int at = 0; at < priority.Length; at++) { priority[at] = checked((byte)reader.ReadInt32()); }
                CeltEnergyReader.ReadCoarse(ref decoder, active, channels, start, end, shift, intra);
                VerifyState(reader, ref decoder, active, index, "coarse");
                CeltEnergyReader.ReadFine(ref decoder, active, fine, channels, start, end);
                VerifyState(reader, ref decoder, active, index, "fine");
                CeltEnergyReader.ReadFinal(ref decoder, active, fine, priority, channels, start, end);
                VerifyState(reader, ref decoder, active, index, "final");
            }
            if (reader.BaseStream.Position != reader.BaseStream.Length)
            {
                throw new InvalidDataException("The energy reference has trailing bytes.");
            }
            return cases;
        }

        private static void VerifyState(BinaryReader reader, ref OpusRangeDecoder decoder,
            scoped ReadOnlySpan<float> energies, int index, string stage)
        {
            for (int at = 0; at < energies.Length; at++)
            {
                int expected = reader.ReadInt32(), actual = BitConverter.SingleToInt32Bits(energies[at]);
                if (actual != expected)
                {
                    throw new InvalidDataException($"Energy case {index}, stage {stage}, band {at}, differs: {energies[at]} / {BitConverter.Int32BitsToSingle(expected)}.");
                }
            }
            if (decoder.Range != reader.ReadUInt32() || decoder.BitsUsed != reader.ReadInt32() ||
                decoder.EighthBitsUsed != reader.ReadInt32())
            {
                throw new InvalidDataException($"Energy case {index}, stage {stage}, has a different entropy state.");
            }
        }
    }
}
