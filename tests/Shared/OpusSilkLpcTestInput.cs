// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Opus;

namespace Mediana.Tests
{
    internal static class OpusSilkLpcTestInput
    {
        public static int Verify(string root)
        {
            using BinaryReader reader = new(File.OpenRead(Path.Combine(root, "silk-lpc.bin")));
            if (!reader.ReadBytes(8).AsSpan().SequenceEqual("OPUSLPC1"u8))
            {
                throw new InvalidDataException("The SILK LPC reference header is invalid.");
            }
            Span<short> frequencies = stackalloc short[16], coefficients = stackalloc short[17];
            int conversions = reader.ReadInt32();
            for (int index = 0; index < conversions; index++)
            {
                int order = reader.ReadInt32();
                for (int at = 0; at < order; at++) { frequencies[at] = reader.ReadInt16(); }
                coefficients.Fill(short.MinValue);
                SilkLpcConverter.Convert(frequencies[..order], coefficients);
                for (int at = 0; at < order; at++)
                {
                    short expected = reader.ReadInt16();
                    if (coefficients[at] != expected)
                    {
                        throw new InvalidDataException($"SILK LPC conversion {index}, coefficient {at}, differs: {coefficients[at]} / {expected}.");
                    }
                }
                if (SilkLpcStability.InverseGainQ30(coefficients[..order]) != reader.ReadInt32())
                {
                    throw new InvalidDataException($"SILK LPC conversion {index} has a different inverse prediction gain.");
                }
                foreach (short value in coefficients[order..])
                {
                    if (value != short.MinValue) { throw new InvalidDataException("SILK LPC conversion overwrote the unused output."); }
                }
            }
            int stabilityCases = reader.ReadInt32();
            for (int index = 0; index < stabilityCases; index++)
            {
                int order = reader.ReadInt32();
                for (int at = 0; at < order; at++) { coefficients[at] = reader.ReadInt16(); }
                int expected = reader.ReadInt32(), actual = SilkLpcStability.InverseGainQ30(coefficients[..order]);
                if (actual != expected)
                {
                    throw new InvalidDataException($"SILK LPC stability case {index} differs: {actual} / {expected}.");
                }
            }
            if (conversions != 4096 || stabilityCases != 2048 || reader.BaseStream.Position != reader.BaseStream.Length)
            {
                throw new InvalidDataException("The SILK LPC reference count or length differs.");
            }
            return conversions + stabilityCases;
        }
    }
}
