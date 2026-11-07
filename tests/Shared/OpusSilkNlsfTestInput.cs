// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Opus;

namespace Mediana.Tests
{
    internal static class OpusSilkNlsfTestInput
    {
        public static int Verify(string root)
        {
            using BinaryReader reader = new(File.OpenRead(Path.Combine(root, "silk-lsf.bin")));
            if (!reader.ReadBytes(8).AsSpan().SequenceEqual("OPUSLSF1"u8))
            {
                throw new InvalidDataException("The SILK LSF reference header is invalid.");
            }
            Span<sbyte> indices = stackalloc sbyte[17];
            Span<short> frequencies = stackalloc short[16];
            int cases = reader.ReadInt32();
            for (int index = 0; index < cases; index++)
            {
                byte[] bytes = reader.ReadBytes(reader.ReadInt32());
                SilkNlsfCodebook codebook = new(reader.ReadInt32());
                SilkSignalType signal = (SilkSignalType)reader.ReadInt32();
                int subframes = reader.ReadInt32();
                OpusRangeDecoder decoder = new(bytes);
                SilkNlsfReader.Read(ref decoder, codebook, signal, indices);
                int interpolation = SilkNlsfReader.ReadInterpolation(ref decoder, subframes);
                for (int at = 0; at <= codebook.Order; at++)
                {
                    if (indices[at] != reader.ReadSByte()) { throw new InvalidDataException($"SILK LSF case {index} has a different index."); }
                }
                if (interpolation != reader.ReadInt32() || decoder.Range != reader.ReadUInt32() ||
                    decoder.BitsUsed != reader.ReadInt32() || decoder.EighthBitsUsed != reader.ReadInt32())
                {
                    throw new InvalidDataException($"SILK LSF case {index} has different interpolation or entropy state.");
                }
                VerifyDequantization(reader, codebook, indices, frequencies, index);
            }
            int dequantizationCases = reader.ReadInt32();
            for (int index = 0; index < dequantizationCases; index++)
            {
                SilkNlsfCodebook codebook = new(reader.ReadInt32());
                for (int at = 0; at <= codebook.Order; at++) { indices[at] = reader.ReadSByte(); }
                VerifyDequantization(reader, codebook, indices, frequencies, cases + index);
            }
            int stabilizationCases = reader.ReadInt32();
            for (int index = 0; index < stabilizationCases; index++)
            {
                SilkNlsfCodebook codebook = new(reader.ReadInt32());
                for (int at = 0; at < codebook.Order; at++) { frequencies[at] = reader.ReadInt16(); }
                SilkNlsfStabilizer.Apply(frequencies[..codebook.Order], codebook.MinimumDistances);
                Compare(reader, frequencies[..codebook.Order], index);
            }
            if (cases != 768 || dequantizationCases != 4096 || stabilizationCases != 1024 || reader.BaseStream.Position != reader.BaseStream.Length)
            {
                throw new InvalidDataException("The SILK LSF reference count or length differs.");
            }
            return cases + dequantizationCases + stabilizationCases;
        }

        private static void VerifyDequantization(BinaryReader reader, SilkNlsfCodebook codebook,
            ReadOnlySpan<sbyte> indices, Span<short> frequencies, int index)
        {
            Span<byte> offsets = stackalloc byte[16], predictors = stackalloc byte[16];
            codebook.Unpack(indices[0], offsets, predictors);
            for (int at = 0; at < codebook.Order; at++)
            {
                if (offsets[at] != reader.ReadInt32() || predictors[at] != reader.ReadByte())
                {
                    throw new InvalidDataException($"SILK LSF case {index} has a different predictor or residual model.");
                }
            }
            SilkNlsfReader.Dequantize(codebook, indices, frequencies);
            Compare(reader, frequencies[..codebook.Order], index);
        }

        private static void Compare(BinaryReader reader, ReadOnlySpan<short> frequencies, int index)
        {
            for (int at = 0; at < frequencies.Length; at++)
            {
                short expected = reader.ReadInt16();
                if (frequencies[at] != expected)
                {
                    throw new InvalidDataException($"SILK LSF case {index}, frequency {at}, differs: {frequencies[at]} / {expected}.");
                }
            }
        }
    }
}
