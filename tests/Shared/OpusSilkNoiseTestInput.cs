// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Opus;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class OpusSilkNoiseTestInput
    {
        public static int Verify(string root)
        {
            using BinaryReader reader = new(File.OpenRead(Path.Combine(root, "silk-noise.bin")));
            if (!reader.ReadBytes(8).AsSpan().SequenceEqual("OPUSCNG1"u8)) { throw new InvalidDataException("The SILK comfort-noise reference header is invalid."); }
            MediaBufferAllocator allocator = new(16 * 1024);
            Span<int> gains = stackalloc int[4]; Span<short> output = stackalloc short[320];
            int count = reader.ReadInt32(), frames = 0;
            for (int index = 0; index < count; index++)
            {
                SilkFrameMode mode = new(reader.ReadInt32(), reader.ReadInt32()); reader.ReadInt32();
                using (SilkSynthesisState synthesis = new(mode.SampleRate, allocator))
                using (SilkRecoveryState recovery = new(mode.SampleRate, allocator))
                {
                    for (int frame = 0; frame < 8; frame++)
                    {
                        for (int at = 0; at < mode.LpcOrder; at++) { synthesis.PreviousLsf[at] = reader.ReadInt16(); }
                        for (int at = 0; at < mode.Subframes; at++) { gains[at] = reader.ReadInt32(); }
                        for (int at = 0; at < mode.FrameSamples; at++) { synthesis.Excitation[at] = reader.ReadInt32(); }
                        SilkComfortNoise.Update(recovery, synthesis, mode, SilkSignalType.Inactive, new(gains, [], [], []));
                    }
                    for (int frame = 0; frame < 4; frame++)
                    {
                        recovery.RandomScaleQ14 = reader.ReadInt32(); recovery.Gain2Q16 = reader.ReadInt32();
                        for (int at = 0; at < mode.FrameSamples; at++) { output[at] = reader.ReadInt16(); }
                        SilkComfortNoise.Synthesize(recovery, mode, output);
                        for (int at = 0; at < mode.FrameSamples; at++)
                        {
                            short expected = reader.ReadInt16();
                            if (output[at] != expected) { throw new InvalidDataException($"SILK comfort-noise {index}, frame {frame}, sample {at}: {output[at]} / {expected}."); }
                        }
                        for (int at = 0; at < 16; at++)
                        {
                            int expected = reader.ReadInt32();
                            if (recovery.NoiseHistory[at] != expected) { throw new InvalidDataException($"SILK comfort-noise {index}, frame {frame}, history {at} differs."); }
                        }
                        if (recovery.NoiseSeed != reader.ReadInt32()) { throw new InvalidDataException("The SILK comfort-noise seed differs."); }
                        frames++;
                    }
                }
                if (allocator.AllocatedBytes != 0) { throw new InvalidDataException("The SILK comfort-noise retained working storage."); }
            }
            if (reader.BaseStream.Position != reader.BaseStream.Length) { throw new InvalidDataException("The SILK comfort-noise reference has trailing bytes."); }
            return frames;
        }
    }
}
