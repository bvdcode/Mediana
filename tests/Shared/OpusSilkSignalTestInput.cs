// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Opus;

namespace Mediana.Tests
{
    internal static class OpusSilkSignalTestInput
    {
        public static int Verify(string root)
        {
            using BinaryReader reader = new(File.OpenRead(Path.Combine(root, "silk-signal.bin")));
            if (!reader.ReadBytes(8).AsSpan().SequenceEqual("OPUSSIG1"u8)) { throw new InvalidDataException("The SILK signal reference header is invalid."); }
            int count = reader.ReadInt32();
            Span<short> samples = stackalloc short[320], coefficients = stackalloc short[16];
            for (int index = 0; index < count; index++)
            {
                int input = reader.ReadInt32(), expectedRoot = reader.ReadInt32(), length = reader.ReadInt32();
                if (SilkSignalMath.SquareRoot(input) != expectedRoot) { throw new InvalidDataException("The SILK square root differs."); }
                for (int at = 0; at < length; at++) { samples[at] = reader.ReadInt16(); }
                int expectedEnergy = reader.ReadInt32(), expectedShift = reader.ReadInt32();
                if (SilkSignalMath.Energy(samples[..length]) != (expectedEnergy, expectedShift)) { throw new InvalidDataException("The SILK signal energy differs."); }
                int order = reader.ReadInt32(), chirp = reader.ReadInt32();
                for (int at = 0; at < order; at++) { coefficients[at] = reader.ReadInt16(); }
                SilkSignalMath.Expand(coefficients[..order], chirp);
                for (int at = 0; at < order; at++)
                {
                    if (coefficients[at] != reader.ReadInt16()) { throw new InvalidDataException("The SILK filter expansion differs."); }
                }
            }
            if (reader.BaseStream.Position != reader.BaseStream.Length) { throw new InvalidDataException("The SILK signal reference has trailing bytes."); }
            return count;
        }
    }
}
