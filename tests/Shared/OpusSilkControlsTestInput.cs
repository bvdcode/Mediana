// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Opus;

namespace Mediana.Tests
{
    internal static class OpusSilkControlsTestInput
    {
        public static int Verify(string root)
        {
            using BinaryReader reader = new(File.OpenRead(Path.Combine(root, "silk-controls.bin")));
            if (!reader.ReadBytes(8).AsSpan().SequenceEqual("OPUSSLC1"u8))
            {
                throw new InvalidDataException("The SILK controls reference header is invalid.");
            }
            Span<byte> gains = stackalloc byte[4];
            Span<int> linearGains = stackalloc int[4], excitation = stackalloc int[320];
            Span<short> pulses = stackalloc short[320];
            int cases = reader.ReadInt32();
            for (int index = 0; index < cases; index++)
            {
                byte[] bytes = reader.ReadBytes(reader.ReadInt32());
                SilkSignalType signal = (SilkSignalType)reader.ReadInt32();
                bool conditional = reader.ReadInt32() != 0;
                int subframes = reader.ReadInt32(), previous = reader.ReadInt32();
                OpusRangeDecoder decoder = new(bytes);
                SilkGainReader.Read(ref decoder, gains, subframes, signal, conditional);
                for (int at = 0; at < subframes; at++)
                {
                    if (gains[at] != reader.ReadByte()) { throw new InvalidDataException($"SILK controls case {index} has a different gain index."); }
                }
                Entropy(reader, ref decoder, index);
                SilkGainReader.Dequantize(gains[..subframes], linearGains, ref previous, conditional);
                if (previous != reader.ReadInt32()) { throw new InvalidDataException($"SILK controls case {index} has a different gain history."); }
                for (int at = 0; at < subframes; at++)
                {
                    if (linearGains[at] != reader.ReadInt32()) { throw new InvalidDataException($"SILK controls case {index} has a different dequantized gain."); }
                }
                SilkStereoPrediction prediction = SilkStereoReader.ReadPrediction(ref decoder);
                bool mid = SilkStereoReader.ReadMidOnly(ref decoder);
                if (prediction.LowQ13 != reader.ReadInt32() || prediction.HighQ13 != reader.ReadInt32() || mid != (reader.ReadInt32() != 0))
                {
                    throw new InvalidDataException($"SILK controls case {index} has different stereo parameters.");
                }
                Entropy(reader, ref decoder, index);
                int samples = reader.ReadInt32(), seed = reader.ReadInt32(), offset = reader.ReadInt32();
                for (int at = 0; at < samples; at++) { pulses[at] = reader.ReadInt16(); }
                SilkExcitation.Reconstruct(pulses[..samples], excitation, signal, offset, seed);
                for (int at = 0; at < samples; at++)
                {
                    int expected = reader.ReadInt32();
                    if (excitation[at] != expected)
                    {
                        throw new InvalidDataException($"SILK controls case {index}, excitation {at}, differs: {excitation[at]} / {expected}.");
                    }
                }
            }
            int exponentCases = reader.ReadInt32();
            for (int at = 0; at < exponentCases; at++)
            {
                int exponent = reader.ReadInt32(), expected = reader.ReadInt32();
                if (SilkFixedMath.Exp2Q7(exponent) != expected) { throw new InvalidDataException($"The SILK Q7 exponent {exponent} differs."); }
            }
            if (exponentCases != 3970 || reader.BaseStream.Position != reader.BaseStream.Length)
            {
                throw new InvalidDataException("The SILK controls reference count or length differs.");
            }
            return cases;
        }

        private static void Entropy(BinaryReader reader, ref OpusRangeDecoder decoder, int index)
        {
            if (decoder.Range != reader.ReadUInt32() || decoder.BitsUsed != reader.ReadInt32() || decoder.EighthBitsUsed != reader.ReadInt32())
            {
                throw new InvalidDataException($"SILK controls case {index} has different entropy state.");
            }
        }
    }
}
