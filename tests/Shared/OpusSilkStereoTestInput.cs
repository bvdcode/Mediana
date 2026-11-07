// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Opus;

namespace Mediana.Tests
{
    internal static class OpusSilkStereoTestInput
    {
        public static int Verify(string root)
        {
            using BinaryReader reader = new(File.OpenRead(Path.Combine(root, "silk-stereo.bin")));
            if (!reader.ReadBytes(8).AsSpan().SequenceEqual("OPUSSTM1"u8))
            {
                throw new InvalidDataException("The SILK stereo reference header is invalid.");
            }
            SilkStereoState state = new();
            Span<short> mid = stackalloc short[321], side = stackalloc short[321];
            int cases = reader.ReadInt32();
            for (int index = 0; index < cases; index++)
            {
                SilkFrameMode mode = new(reader.ReadInt32(), reader.ReadInt32());
                if (reader.ReadInt32() == 0) { state.Reset(); }
                SilkStereoPrediction prediction = new(reader.ReadInt32(), reader.ReadInt32());
                mid.Fill(short.MinValue); side.Fill(short.MinValue);
                Read(reader, mid[..mode.FrameSamples]); Read(reader, side[..mode.FrameSamples]);
                SilkStereoSynthesis.Unmix(state, mode, prediction, mid, side);
                Compare(reader, mid[..mode.FrameSamples], index); Compare(reader, side[..mode.FrameSamples], index);
                if (state.MidPrevious2 != reader.ReadInt16() || state.MidPrevious1 != reader.ReadInt16() ||
                    state.SidePrevious2 != reader.ReadInt16() || state.SidePrevious1 != reader.ReadInt16() ||
                    state.Prediction != new SilkStereoPrediction(reader.ReadInt32(), reader.ReadInt32()))
                {
                    throw new InvalidDataException($"SILK stereo case {index} has different final history.");
                }
                for (int at = mode.FrameSamples; at < mid.Length; at++)
                {
                    if (mid[at] != short.MinValue || side[at] != short.MinValue)
                    {
                        throw new InvalidDataException("SILK stereo unmixing overwrote unused signal storage.");
                    }
                }
            }
            if (reader.BaseStream.Position != reader.BaseStream.Length) { throw new InvalidDataException("The SILK stereo reference has trailing bytes."); }
            return cases;
        }

        private static void Read(BinaryReader reader, Span<short> values)
        {
            for (int at = 0; at < values.Length; at++) { values[at] = reader.ReadInt16(); }
        }

        private static void Compare(BinaryReader reader, ReadOnlySpan<short> values, int index)
        {
            for (int at = 0; at < values.Length; at++)
            {
                short expected = reader.ReadInt16();
                if (values[at] != expected)
                {
                    throw new InvalidDataException($"SILK stereo case {index}, sample {at}, differs: {values[at]} / {expected}.");
                }
            }
        }
    }
}
