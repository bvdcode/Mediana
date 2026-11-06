// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Opus;

namespace Mediana.Tests
{
    internal static class OpusSilkFixedTestInput
    {
        public static int Verify(string root)
        {
            using BinaryReader reader = new(File.OpenRead(Path.Combine(root, "silk-fixed.bin")));
            if (!reader.ReadBytes(8).AsSpan().SequenceEqual("OPUSFIX1"u8))
            {
                throw new InvalidDataException("The SILK fixed arithmetic reference header is invalid.");
            }
            int cases = reader.ReadInt32();
            for (int index = 0; index < cases; index++)
            {
                int actual = reader.ReadInt32() switch
                {
                    0 => SilkFixedMath.InversePositive(reader.ReadInt32(), reader.ReadInt32()),
                    1 => SilkFixedMath.DividePositive(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32()),
                    _ => throw new InvalidDataException("The SILK fixed arithmetic operation is invalid.")
                };
                int expected = reader.ReadInt32();
                if (actual != expected)
                {
                    throw new InvalidDataException($"SILK fixed arithmetic case {index} differs: {actual} / {expected}.");
                }
            }
            if (reader.BaseStream.Position != reader.BaseStream.Length) { throw new InvalidDataException("The SILK fixed arithmetic reference has trailing bytes."); }
            return cases;
        }
    }
}
