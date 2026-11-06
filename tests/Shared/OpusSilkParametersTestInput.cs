// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Opus;

namespace Mediana.Tests
{
    internal static class OpusSilkParametersTestInput
    {
        public static int Verify(string root)
        {
            using BinaryReader reader = new(File.OpenRead(Path.Combine(root, "silk-parameters.bin")));
            if (!reader.ReadBytes(8).AsSpan().SequenceEqual("OPUSSLM1"u8))
            {
                throw new InvalidDataException("The SILK parameter reference header is invalid.");
            }
            Span<byte> gainIndices = stackalloc byte[4], filters = stackalloc byte[4];
            Span<sbyte> lsf = stackalloc sbyte[17];
            Span<short> previousLsf = stackalloc short[16], lpc = stackalloc short[32], ltp = stackalloc short[20];
            Span<int> gains = stackalloc int[4], lags = stackalloc int[4];
            int cases = reader.ReadInt32();
            for (int index = 0; index < cases; index++)
            {
                SilkFrameMode mode = new(reader.ReadInt32(), reader.ReadInt32());
                SilkCodingMode coding = (SilkCodingMode)reader.ReadInt32();
                bool firstFrame = reader.ReadInt32() != 0;
                int previousGain = reader.ReadInt32();
                Read(reader, previousLsf[..mode.LpcOrder]);
                SilkSignalType signal = (SilkSignalType)reader.ReadInt32();
                byte offset = (byte)reader.ReadInt32(), interpolation = (byte)reader.ReadInt32(), seed = (byte)reader.ReadInt32();
                SilkPitchIndices pitch = new((short)reader.ReadInt32(), (byte)reader.ReadInt32(), (byte)reader.ReadInt32(), (byte)reader.ReadInt32());
                for (int at = 0; at < mode.Subframes; at++) { gainIndices[at] = reader.ReadByte(); }
                for (int at = 0; at <= mode.LpcOrder; at++) { lsf[at] = reader.ReadSByte(); }
                for (int at = 0; at < mode.Subframes; at++) { filters[at] = reader.ReadByte(); }
                SilkFrameIndices indices = new(signal, offset, interpolation, seed, pitch);
                gains.Fill(int.MinValue); lpc.Fill(short.MinValue); lags.Fill(int.MinValue); ltp.Fill(short.MinValue);
                SilkFilterParameters result = SilkParameterReconstruction.Reconstruct(mode, coding, indices,
                    new(gainIndices, lsf, filters), new(gains, lpc, lags, ltp), previousLsf, ref previousGain, firstFrame);
                if (result.InterpolationQ2 != reader.ReadInt32() || previousGain != reader.ReadInt32() || result.LtpScaleQ14 != reader.ReadInt32())
                {
                    throw new InvalidDataException($"SILK parameters case {index} has different filter controls or gain history.");
                }
                Compare(reader, previousLsf[..mode.LpcOrder], index);
                Compare(reader, gains[..mode.Subframes], index); Compare(reader, lpc[..(2 * mode.LpcOrder)], index);
                Compare(reader, lags[..mode.Subframes], index); Compare(reader, ltp[..(5 * mode.Subframes)], index);
                foreach (int value in gains[mode.Subframes..])
                {
                    if (value != int.MinValue) { throw new InvalidDataException("SILK parameter reconstruction overwrote unused gain storage."); }
                }
                foreach (short value in lpc[(2 * mode.LpcOrder)..])
                {
                    if (value != short.MinValue) { throw new InvalidDataException("SILK parameter reconstruction overwrote unused LPC storage."); }
                }
            }
            if (reader.BaseStream.Position != reader.BaseStream.Length) { throw new InvalidDataException("The SILK parameter reference has trailing bytes."); }
            return cases;
        }

        private static void Read(BinaryReader reader, Span<short> destination)
        {
            for (int at = 0; at < destination.Length; at++) { destination[at] = reader.ReadInt16(); }
        }

        private static void Compare(BinaryReader reader, ReadOnlySpan<short> values, int index)
        {
            for (int at = 0; at < values.Length; at++)
            {
                short expected = reader.ReadInt16();
                if (values[at] != expected) { throw new InvalidDataException($"SILK parameters case {index}, coefficient {at}, differs: {values[at]} / {expected}."); }
            }
        }

        private static void Compare(BinaryReader reader, ReadOnlySpan<int> values, int index)
        {
            for (int at = 0; at < values.Length; at++)
            {
                int expected = reader.ReadInt32();
                if (values[at] != expected) { throw new InvalidDataException($"SILK parameters case {index}, value {at}, differs: {values[at]} / {expected}."); }
            }
        }
    }
}
