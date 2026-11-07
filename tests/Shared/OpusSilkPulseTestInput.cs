// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Opus;

namespace Mediana.Tests
{
    internal static class OpusSilkPulseTestInput
    {
        public static int Verify(string root)
        {
            using BinaryReader reader = new(File.OpenRead(Path.Combine(root, "silk-pulses.bin")));
            if (!reader.ReadBytes(8).AsSpan().SequenceEqual("OPUSSLP1"u8))
            {
                throw new InvalidDataException("The SILK pulse reference header is invalid.");
            }
            Span<short> pulses = stackalloc short[321];
            int count = reader.ReadInt32();
            for (int index = 0; index < count; index++)
            {
                byte[] bytes = reader.ReadBytes(reader.ReadInt32());
                int samples = reader.ReadInt32();
                SilkSignalType type = (SilkSignalType)reader.ReadInt32();
                int offset = reader.ReadInt32(), padded = (samples + 15) & ~15;
                pulses.Fill(short.MinValue);
                OpusRangeDecoder decoder = new(bytes);
                SilkPulseReader.Read(ref decoder, pulses, samples, type, offset);
                if (decoder.Range != reader.ReadUInt32() || decoder.BitsUsed != reader.ReadInt32() || decoder.EighthBitsUsed != reader.ReadInt32())
                {
                    throw new InvalidDataException($"SILK pulse case {index} has different entropy state.");
                }
                for (int at = 0; at < padded; at++)
                {
                    short expected = reader.ReadInt16();
                    if (pulses[at] != expected)
                    {
                        throw new InvalidDataException($"SILK pulse case {index}, sample {at}, differs: {pulses[at]} / {expected}.");
                    }
                }
                foreach (short value in pulses[padded..])
                {
                    if (value != short.MinValue) { throw new InvalidDataException("SILK pulse decoding overwrote the unused destination."); }
                }
            }
            if (reader.BaseStream.Position != reader.BaseStream.Length) { throw new InvalidDataException("The SILK pulse reference has trailing bytes."); }
            return count;
        }
    }
}
