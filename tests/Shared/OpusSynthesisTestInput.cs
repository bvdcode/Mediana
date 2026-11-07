// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Opus;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class OpusSynthesisTestInput
    {
        public static int Verify(string root)
        {
            using BinaryReader reader = new(File.OpenRead(Path.Combine(root, "synthesis.bin")));
            if (!reader.ReadBytes(8).AsSpan().SequenceEqual("OPUSSYN1"u8))
            {
                throw new InvalidDataException("The CELT synthesis reference header is invalid.");
            }
            MediaBufferAllocator allocator = new(32 * 1024);
            using CeltTransformPlan plan = CeltTransformPlan.Create(allocator);
            const int stride = CeltPitchPostfilter.HistorySamples + 960 + 60;
            Span<float> signal = stackalloc float[2 * stride], spectrum = stackalloc float[1920];
            Span<float> frequency = stackalloc float[1920], workspace = stackalloc float[960];
            Span<float> pcm = stackalloc float[1920], energies = stackalloc float[42], memory = stackalloc float[2];
            int cases = reader.ReadInt32();
            for (int index = 0; index < cases; index++)
            {
                int codedChannels = reader.ReadInt32(), outputChannels = reader.ReadInt32();
                CeltBandMode mode = new(codedChannels, reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
                bool transient = reader.ReadInt32() != 0;
                CeltPostFilterParameters old = Parameters(reader), current = Parameters(reader), next = Parameters(reader);
                int samples = 120 << mode.FrameShift;
                Read(reader, energies[..(21 * codedChannels)]); Read(reader, spectrum[..(samples * codedChannels)]);
                signal.Fill(float.NaN);
                for (int channel = 0; channel < outputChannels; channel++)
                {
                    Read(reader, signal.Slice(channel * stride, CeltPitchPostfilter.HistorySamples + 60));
                }
                Read(reader, memory[..outputChannels]);
                CeltBandDenormalizer.Apply(mode, spectrum, energies, frequency, false);
                CeltSynthesis.Apply(plan, mode, transient, outputChannels, frequency,
                    signal[CeltPitchPostfilter.HistorySamples..], stride, workspace);
                for (int channel = 0; channel < outputChannels; channel++)
                {
                    Span<float> channelSignal = signal.Slice(channel * stride, stride);
                    CeltPitchPostfilter.Apply(channelSignal, CeltPitchPostfilter.HistorySamples, 120, old, current, plan.Window);
                    if (mode.FrameShift > 0)
                    {
                        CeltPitchPostfilter.Apply(channelSignal, CeltPitchPostfilter.HistorySamples + 120,
                            samples - 120, current, next, plan.Window);
                    }
                    Compare(reader, channelSignal.Slice(CeltPitchPostfilter.HistorySamples, samples + 60), index, "signal", true);
                }
                CeltDeemphasis.Apply(signal[CeltPitchPostfilter.HistorySamples..], samples, outputChannels, stride, pcm, memory);
                Compare(reader, pcm[..(samples * outputChannels)], index, "PCM");
                Compare(reader, memory[..outputChannels], index, "de-emphasis memory");
            }
            if (reader.BaseStream.Position != reader.BaseStream.Length)
            {
                throw new InvalidDataException("The CELT synthesis reference has trailing bytes.");
            }
            plan.Dispose();
            if (allocator.AllocatedBytes != 0) { throw new InvalidDataException("The CELT synthesis plan retained its allocation."); }
            return cases;
        }

        private static CeltPostFilterParameters Parameters(BinaryReader reader) =>
            new(reader.ReadInt32(), reader.ReadSingle(), reader.ReadInt32());

        private static void Read(BinaryReader reader, Span<float> destination)
        {
            for (int at = 0; at < destination.Length; at++) { destination[at] = reader.ReadSingle(); }
        }

        private static void Compare(BinaryReader reader, Span<float> values, int index, string stage, bool loadReference = false)
        {
            double errorEnergy = 0, signalEnergy = 0;
            float peak = 0, errorPeak = 0;
            for (int at = 0; at < values.Length; at++)
            {
                float expected = reader.ReadSingle(), actual = values[at];
                if (!float.IsFinite(actual)) { throw new InvalidDataException($"CELT synthesis case {index}, {stage} {at}, is not finite."); }
                double difference = (double)actual - expected;
                errorEnergy += difference * difference; signalEnergy += (double)expected * expected;
                errorPeak = Math.Max(errorPeak, (float)Math.Abs(difference)); peak = Math.Max(peak, Math.Abs(expected));
                if (loadReference) { values[at] = expected; }
            }
            double relative = Math.Sqrt(errorEnergy / Math.Max(1e-20, signalEnergy));
            if (relative > .000003 || errorPeak > .0001f * Math.Max(.00001f, peak))
            {
                throw new InvalidDataException($"CELT synthesis case {index}, {stage}, relative error {relative}, peak error {errorPeak} / {peak}.");
            }
        }
    }
}
