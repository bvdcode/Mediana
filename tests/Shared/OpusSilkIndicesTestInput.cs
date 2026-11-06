// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Opus;

namespace Mediana.Tests
{
    internal static class OpusSilkIndicesTestInput
    {
        public static int Verify(string root)
        {
            using BinaryReader reader = new(File.OpenRead(Path.Combine(root, "silk-indices.bin")));
            if (!reader.ReadBytes(8).AsSpan().SequenceEqual("OPUSSLI1"u8))
            {
                throw new InvalidDataException("The SILK indices reference header is invalid.");
            }
            Span<byte> gains = stackalloc byte[4], filters = stackalloc byte[4];
            Span<sbyte> lsf = stackalloc sbyte[17];
            Span<short> pulses = stackalloc short[320], coefficients = stackalloc short[20];
            Span<int> lags = stackalloc int[4];
            int cases = reader.ReadInt32();
            for (int index = 0; index < cases; index++)
            {
                byte[] bytes = reader.ReadBytes(reader.ReadInt32());
                SilkFrameMode mode = new(reader.ReadInt32(), reader.ReadInt32());
                SilkCodingMode coding = (SilkCodingMode)reader.ReadInt32();
                bool active = reader.ReadInt32() != 0;
                SilkEntropyHistory history = new((SilkSignalType)reader.ReadInt32(), (short)reader.ReadInt32());
                OpusRangeDecoder decoder = new(bytes);
                SilkFrameIndices indices = SilkFrameIndicesReader.Read(ref decoder, mode, coding, active, ref history, new(gains, lsf, filters));
                if ((int)indices.Signal != reader.ReadInt32() || indices.QuantizationOffset != reader.ReadInt32() ||
                    indices.InterpolationQ2 != reader.ReadInt32() || indices.Seed != reader.ReadInt32())
                {
                    throw new InvalidDataException($"SILK indices case {index} has different frame parameters.");
                }
                if (indices.Pitch.LagIndex != reader.ReadInt32() || indices.Pitch.ContourIndex != reader.ReadInt32() ||
                    indices.Pitch.PeriodicityIndex != reader.ReadInt32() || indices.Pitch.ScaleIndex != reader.ReadInt32())
                {
                    throw new InvalidDataException($"SILK indices case {index} has different pitch parameters.");
                }
                for (int at = 0; at < mode.Subframes; at++)
                {
                    if (gains[at] != reader.ReadByte()) { throw new InvalidDataException($"SILK indices case {index} has a different gain index."); }
                }
                for (int at = 0; at <= mode.LpcOrder; at++)
                {
                    if (lsf[at] != reader.ReadSByte()) { throw new InvalidDataException($"SILK indices case {index} has a different LSF index."); }
                }
                for (int at = 0; at < mode.Subframes; at++)
                {
                    if (filters[at] != reader.ReadByte()) { throw new InvalidDataException($"SILK indices case {index} has a different LTP index."); }
                }
                if ((int)history.Signal != reader.ReadInt32() || history.LagIndex != reader.ReadInt32())
                {
                    throw new InvalidDataException($"SILK indices case {index} has different entropy history.");
                }
                Entropy(reader, ref decoder, index);
                SilkPulseReader.Read(ref decoder, pulses, mode.FrameSamples, indices.Signal, indices.QuantizationOffset);
                int padded = (mode.FrameSamples + 15) & ~15;
                for (int at = 0; at < padded; at++)
                {
                    if (pulses[at] != reader.ReadInt16()) { throw new InvalidDataException($"SILK indices case {index} has a different excitation pulse."); }
                }
                Entropy(reader, ref decoder, index);
                if (indices.Signal == SilkSignalType.Voiced)
                {
                    int scale = SilkPitchReader.Dequantize(mode, indices.Pitch, filters, lags, coefficients);
                    for (int at = 0; at < mode.Subframes; at++)
                    {
                        if (lags[at] != reader.ReadInt32()) { throw new InvalidDataException($"SILK indices case {index} has a different pitch lag."); }
                    }
                    for (int at = 0; at < mode.Subframes * 5; at++)
                    {
                        if (coefficients[at] != reader.ReadInt16()) { throw new InvalidDataException($"SILK indices case {index} has a different pitch-filter coefficient."); }
                    }
                    if (scale != reader.ReadInt32()) { throw new InvalidDataException($"SILK indices case {index} has different LTP scaling."); }
                }
            }
            if (reader.BaseStream.Position != reader.BaseStream.Length) { throw new InvalidDataException("The SILK indices reference has trailing bytes."); }
            return cases;
        }

        private static void Entropy(BinaryReader reader, ref OpusRangeDecoder decoder, int index)
        {
            if (decoder.Range != reader.ReadUInt32() || decoder.BitsUsed != reader.ReadInt32() || decoder.EighthBitsUsed != reader.ReadInt32())
            {
                throw new InvalidDataException($"SILK indices case {index} has different entropy state.");
            }
        }
    }
}
