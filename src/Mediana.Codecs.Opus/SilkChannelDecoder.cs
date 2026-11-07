// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Codecs.Opus
{
    internal class SilkChannelDecoder : IDisposable
    {
        private readonly SilkSynthesisState synthesis;
        private readonly SilkRecoveryState recovery;
        private SilkEntropyHistory entropyHistory;
        private int previousGainIndex = 10;
        private bool firstFrame = true;

        public SilkChannelDecoder(int sampleRate, MediaBufferAllocator allocator)
        {
            SilkSynthesisState created = new(sampleRate, allocator);
            bool succeeded = false;
            try
            {
                recovery = new(sampleRate, allocator); synthesis = created; succeeded = true;
            }
            finally { if (!succeeded) { created.Dispose(); } }
        }

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
            int recoveryPitch = 0;
            if (recovery.LossCount > 0)
            {
                SilkSignalMath.Expand(parameters.LpcQ12[..mode.LpcOrder], 63570);
                SilkSignalMath.Expand(parameters.LpcQ12.Slice(mode.LpcOrder, mode.LpcOrder), 63570);
                if (recovery.PreviousSignal == SilkSignalType.Voiced && indices.Signal != SilkSignalType.Voiced)
                {
                    recoveryPitch = recovery.PreviousLag;
                    for (int at = 0; at < 2; at++)
                    {
                        parameters.PitchLags[at] = recoveryPitch;
                        Span<short> taps = parameters.LtpQ14.Slice(5 * at, 5); taps.Clear(); taps[2] = 4096;
                    }
                }
            }
            SilkSynthesis.Process(synthesis, mode, indices, filters, parameters, pulses, output, recoveryPitch);
            SilkConcealment.Update(recovery, mode, indices, filters, parameters);
            recovery.LossCount = 0;
            SilkComfortNoise.Update(recovery, synthesis, mode, indices.Signal, parameters);
            SilkConcealment.Connect(recovery, output[..mode.FrameSamples]);
            firstFrame = false;
        }

        public void Conceal(SilkFrameMode mode, Span<short> output)
        {
            SilkConcealment.Synthesize(recovery, synthesis, mode, firstFrame, output);
            SilkComfortNoise.Synthesize(recovery, mode, output);
            SilkConcealment.Connect(recovery, output[..mode.FrameSamples]);
            previousGainIndex = 10;
        }

        public void ResetGainIndex() => previousGainIndex = 10;

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
            synthesis.Reset(); recovery.Reset(synthesis.SampleRate); entropyHistory = default; previousGainIndex = 10; firstFrame = true;
        }

        public void SetSampleRate(int sampleRate)
        {
            synthesis.SetSampleRate(sampleRate); recovery.SetSampleRate(sampleRate); previousGainIndex = 10; firstFrame = true;
        }

        public void ResetPrediction()
        {
            synthesis.ClearPrediction(); recovery.PreviousSignal = SilkSignalType.Inactive; recovery.PreviousLag = 100; previousGainIndex = 10; firstFrame = true;
        }

        public void Dispose() { recovery.Dispose(); synthesis.Dispose(); }
    }
}
