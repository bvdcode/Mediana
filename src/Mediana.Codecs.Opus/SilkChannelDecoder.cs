// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Codecs.Opus
{
    internal class SilkChannelDecoder(int sampleRate, MediaBufferAllocator allocator) : IDisposable
    {
        private readonly SilkSynthesisState synthesis = new(sampleRate, allocator);
        private SilkEntropyHistory entropyHistory;
        private int previousGainIndex = 10;
        private bool firstFrame = true;

        public void Decode(ref OpusRangeDecoder range, SilkFrameMode mode, SilkCodingMode coding, bool active, Span<short> output)
        {
            if (mode.SampleRate != synthesis.SampleRate) { throw new ArgumentException("The SILK channel has a different sample rate.", nameof(mode)); }
            ArgumentOutOfRangeException.ThrowIfLessThan(output.Length, mode.FrameSamples, nameof(output));
            Span<byte> gains = stackalloc byte[4], pitchFilters = stackalloc byte[4];
            Span<sbyte> lsf = stackalloc sbyte[17];
            Span<short> pulses = stackalloc short[320], lpc = stackalloc short[32], ltp = stackalloc short[20];
            Span<int> gainValues = stackalloc int[4], lags = stackalloc int[4];
            SilkIndexBuffers raw = new(gains, lsf, pitchFilters);
            SilkFrameIndices indices = SilkFrameIndicesReader.Read(ref range, mode, coding, active, ref entropyHistory, raw);
            SilkPulseReader.Read(ref range, pulses, mode.FrameSamples, indices.Signal, indices.QuantizationOffset);
            SilkParameterBuffers parameters = new(gainValues, lpc, lags, ltp);
            SilkFilterParameters filters = SilkParameterReconstruction.Reconstruct(mode, coding, indices, raw, parameters,
                synthesis.PreviousLsf, ref previousGainIndex, firstFrame);
            SilkSynthesis.Process(synthesis, mode, indices, filters, parameters, pulses, output);
            firstFrame = false;
        }

        public void Skip(ref OpusRangeDecoder range, SilkFrameMode mode, SilkCodingMode coding)
        {
            Span<byte> gains = stackalloc byte[4], pitchFilters = stackalloc byte[4];
            Span<sbyte> lsf = stackalloc sbyte[17];
            Span<short> pulses = stackalloc short[320];
            SilkFrameIndices indices = SilkFrameIndicesReader.Read(ref range, mode, coding, true, ref entropyHistory, new(gains, lsf, pitchFilters));
            SilkPulseReader.Read(ref range, pulses, mode.FrameSamples, indices.Signal, indices.QuantizationOffset);
        }

        public void Reset()
        {
            synthesis.Reset(); entropyHistory = default; previousGainIndex = 10; firstFrame = true;
        }

        public void SetSampleRate(int sampleRate)
        {
            synthesis.SetSampleRate(sampleRate); previousGainIndex = 10; firstFrame = true;
        }

        public void ResetPrediction()
        {
            synthesis.ClearPrediction(); previousGainIndex = 10; firstFrame = true;
        }

        public void Dispose() => synthesis.Dispose();
    }
}
