// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Opus;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class OpusSilkSynthesisTestInput
    {
        public static int Verify(string root)
        {
            using BinaryReader reader = new(File.OpenRead(Path.Combine(root, "silk-synthesis.bin")));
            if (!reader.ReadBytes(8).AsSpan().SequenceEqual("OPUSSLS1"u8))
            {
                throw new InvalidDataException("The SILK synthesis reference header is invalid.");
            }
            MediaBufferAllocator allocator = new(16 * 1024);
            Span<int> gains = stackalloc int[4], lags = stackalloc int[4];
            Span<short> lpc = stackalloc short[32], ltp = stackalloc short[20], pulses = stackalloc short[320], pcm = stackalloc short[321];
            int cases = reader.ReadInt32();
            for (int index = 0; index < cases; index++)
            {
                SilkFrameMode mode = new(reader.ReadInt32(), reader.ReadInt32());
                SilkSignalType signal = (SilkSignalType)reader.ReadInt32();
                byte offset = (byte)reader.ReadInt32(), seed = (byte)reader.ReadInt32(), interpolation = (byte)reader.ReadInt32();
                SilkFilterParameters filters = new(interpolation, reader.ReadInt32());
                SilkFrameIndices indices = new(signal, offset, interpolation, seed, default);
                Read(reader, gains[..mode.Subframes]); Read(reader, lpc[..(2 * mode.LpcOrder)]);
                Read(reader, lags[..mode.Subframes]); Read(reader, ltp[..(5 * mode.Subframes)]); Read(reader, pulses[..mode.FrameSamples]);
                using (SilkSynthesisState state = new(mode.SampleRate, allocator))
                {
                    state.PreviousGainQ16 = reader.ReadInt32();
                    Read(reader, state.OutputHistory[..state.HistorySamples]); Read(reader, state.LpcHistory);
                    pcm.Fill(short.MinValue);
                    SilkSynthesis.Process(state, mode, indices, filters, new(gains, lpc, lags, ltp), pulses, pcm);
                    Compare(reader, pcm[..mode.FrameSamples], index, "PCM");
                    Compare(reader, state.LpcHistory, index);
                    if (state.PreviousGainQ16 != reader.ReadInt32()) { throw new InvalidDataException($"SILK synthesis case {index} has a different final gain."); }
                    Compare(reader, state.OutputHistory[..state.HistorySamples], index, "output history");
                    foreach (short value in pcm[mode.FrameSamples..])
                    {
                        if (value != short.MinValue) { throw new InvalidDataException("SILK synthesis overwrote unused PCM storage."); }
                    }
                }
                if (allocator.AllocatedBytes != 0) { throw new InvalidDataException("SILK synthesis retained its working allocation."); }
            }
            if (reader.BaseStream.Position != reader.BaseStream.Length) { throw new InvalidDataException("The SILK synthesis reference has trailing bytes."); }
            return cases;
        }

        private static void Read(BinaryReader reader, Span<short> destination)
        {
            for (int at = 0; at < destination.Length; at++) { destination[at] = reader.ReadInt16(); }
        }

        private static void Read(BinaryReader reader, Span<int> destination)
        {
            for (int at = 0; at < destination.Length; at++) { destination[at] = reader.ReadInt32(); }
        }

        private static void Compare(BinaryReader reader, ReadOnlySpan<short> values, int index, string stage)
        {
            for (int at = 0; at < values.Length; at++)
            {
                short expected = reader.ReadInt16();
                if (values[at] != expected)
                {
                    throw new InvalidDataException($"SILK synthesis case {index}, {stage} {at}, differs: {values[at]} / {expected}.");
                }
            }
        }

        private static void Compare(BinaryReader reader, ReadOnlySpan<int> values, int index)
        {
            for (int at = 0; at < values.Length; at++)
            {
                int expected = reader.ReadInt32();
                if (values[at] != expected)
                {
                    throw new InvalidDataException($"SILK synthesis case {index}, LPC history {at}, differs: {values[at]} / {expected}.");
                }
            }
        }
    }
}
