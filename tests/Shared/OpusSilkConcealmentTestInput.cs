// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Opus;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class OpusSilkConcealmentTestInput
    {
        public static int Verify(string root)
        {
            using BinaryReader reader = new(File.OpenRead(Path.Combine(root, "silk-concealment.bin")));
            if (!reader.ReadBytes(8).AsSpan().SequenceEqual("OPUSSLC1"u8)) { throw new InvalidDataException("The SILK concealment reference header is invalid."); }
            MediaBufferAllocator allocator = new(16 * 1024);
            Span<int> gains = stackalloc int[4], lags = stackalloc int[4];
            Span<short> lpc = stackalloc short[32], ltp = stackalloc short[20], output = stackalloc short[320];
            int cases = reader.ReadInt32(), frames = 0;
            for (int index = 0; index < cases; index++)
            {
                SilkFrameMode mode = new(reader.ReadInt32(), reader.ReadInt32());
                SilkSignalType signal = (SilkSignalType)reader.ReadInt32(); reader.ReadInt32();
                Read(reader, gains[..mode.Subframes]); Read(reader, lpc.Slice(mode.LpcOrder, mode.LpcOrder));
                Read(reader, lags[..mode.Subframes]); Read(reader, ltp[..(5 * mode.Subframes)]);
                SilkFilterParameters filters = new(4, reader.ReadInt32());
                using (SilkSynthesisState synthesis = new(mode.SampleRate, allocator))
                using (SilkRecoveryState recovery = new(mode.SampleRate, allocator))
                {
                    Read(reader, synthesis.Excitation); Read(reader, synthesis.OutputHistory[..synthesis.HistorySamples]); Read(reader, synthesis.LpcHistory);
                    SilkConcealment.Update(recovery, mode, new(signal, 0, 4, 0, default), filters, new(gains, lpc, lags, ltp));
                    for (int frame = 0; frame < 4; frame++)
                    {
                        SilkConcealment.Synthesize(recovery, synthesis, mode, false, output);
                        Compare(reader, output[..mode.FrameSamples], index, frame, "PCM");
                        foreach (int actual in synthesis.LpcHistory)
                        {
                            int expected = reader.ReadInt32();
                            if (actual != expected) { throw new InvalidDataException($"SILK concealment {index}, frame {frame}, LPC history {actual} / {expected}."); }
                        }
                        Compare(reader, synthesis.OutputHistory[..synthesis.HistorySamples], index, frame, "output history");
                        int pitch = reader.ReadInt32(), randomScale = reader.ReadInt32(), seed = reader.ReadInt32();
                        if (recovery.PitchQ8 != pitch || recovery.RandomScaleQ14 != randomScale || recovery.RandomSeed != seed)
                        {
                            throw new InvalidDataException($"SILK concealment {index}, frame {frame}, pitch/random state differs.");
                        }
                        Compare(reader, recovery.Ltp, index, frame, "LTP"); Compare(reader, recovery.Lpc[..mode.LpcOrder], index, frame, "LPC");
                        frames++;
                    }
                }
                if (allocator.AllocatedBytes != 0) { throw new InvalidDataException("The SILK concealment retained working storage."); }
            }
            if (reader.BaseStream.Position != reader.BaseStream.Length) { throw new InvalidDataException("The SILK concealment reference has trailing bytes."); }
            return frames;
        }

        private static void Read(BinaryReader reader, Span<short> values)
        {
            for (int at = 0; at < values.Length; at++) { values[at] = reader.ReadInt16(); }
        }

        private static void Read(BinaryReader reader, Span<int> values)
        {
            for (int at = 0; at < values.Length; at++) { values[at] = reader.ReadInt32(); }
        }

        private static void Compare(BinaryReader reader, ReadOnlySpan<short> values, int index, int frame, string stage)
        {
            for (int at = 0; at < values.Length; at++)
            {
                short expected = reader.ReadInt16();
                if (values[at] != expected) { throw new InvalidDataException($"SILK concealment {index}, frame {frame}, {stage} {at}: {values[at]} / {expected}."); }
            }
        }
    }
}
