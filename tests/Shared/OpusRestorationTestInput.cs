// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Opus;

namespace Mediana.Tests
{
    internal static class OpusRestorationTestInput
    {
        public static int Verify(string root)
        {
            using BinaryReader reader = new(File.OpenRead(Path.Combine(root, "spectrum-restoration.bin")));
            if (!reader.ReadBytes(8).AsSpan().SequenceEqual("OPUSRST1"u8))
            {
                throw new InvalidDataException("The CELT restoration reference header is invalid.");
            }
            Span<float> energies = stackalloc float[42], previous = stackalloc float[42], previous2 = stackalloc float[42];
            Span<float> spectrum = stackalloc float[1920], frequency = stackalloc float[1920];
            Span<byte> masks = stackalloc byte[42];
            Span<int> shape = stackalloc int[21];
            int cases = reader.ReadInt32();
            for (int index = 0; index < cases; index++)
            {
                CeltBandMode mode = new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
                uint seed = reader.ReadUInt32();
                bool silence = reader.ReadInt32() != 0;
                int count = mode.Channels * (120 << mode.FrameShift);
                Read(reader, energies[..(mode.Channels * 21)]); Read(reader, previous); Read(reader, previous2);
                for (int band = 0; band < 21; band++) { shape[band] = reader.ReadInt32(); }
                for (int at = 0; at < mode.Channels * 21; at++) { masks[at] = reader.ReadByte(); }
                Read(reader, spectrum[..count]);
                CeltAntiCollapse.Apply(mode, new(spectrum, default, masks), energies, previous, previous2, shape, seed);
                Verify(reader, spectrum[..count], index, false);
                frequency.Fill(float.NaN);
                CeltBandDenormalizer.Apply(mode, spectrum, energies, frequency, silence);
                Verify(reader, frequency[..count], index, true);
            }
            if (reader.BaseStream.Position != reader.BaseStream.Length)
            {
                throw new InvalidDataException("The CELT restoration reference has trailing bytes.");
            }
            return cases;
        }

        private static void Read(BinaryReader reader, Span<float> destination)
        {
            for (int at = 0; at < destination.Length; at++) { destination[at] = reader.ReadSingle(); }
        }

        private static void Verify(BinaryReader reader, ReadOnlySpan<float> values, int index, bool relative)
        {
            for (int at = 0; at < values.Length; at++)
            {
                float expected = reader.ReadSingle(), tolerance = .000001f;
                if (relative) { tolerance += Math.Abs(expected) * .000001f; }
                if (!float.IsFinite(values[at]) || Math.Abs(values[at] - expected) > tolerance)
                {
                    throw new InvalidDataException($"CELT restoration case {index}, coefficient {at}, differs: {values[at]} / {expected}.");
                }
            }
        }
    }
}
