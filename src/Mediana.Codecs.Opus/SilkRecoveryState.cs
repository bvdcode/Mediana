// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Opus
{
    internal class SilkRecoveryState : IDisposable
    {
        private const int LtpOffset = 16 * sizeof(short);
        private const int NoiseLsfOffset = LtpOffset + 6 * sizeof(short);
        private const int NoiseExcitationOffset = NoiseLsfOffset + 16 * sizeof(short);
        private const int NoiseHistoryOffset = NoiseExcitationOffset + SilkFrameMode.MaximumFrameSamples * sizeof(int);
        private readonly WritableMediaBuffer storage;

        public SilkRecoveryState(int sampleRate, MediaBufferAllocator allocator)
        {
            _ = new SilkFrameMode(sampleRate, 4);
            storage = allocator.Allocate(NoiseHistoryOffset + 16 * sizeof(int));
            Reset(sampleRate);
        }

        public int PitchQ8 { get; set; }
        public int PreviousLag { get; set; }
        public int Gain1Q16 { get; set; }
        public int Gain2Q16 { get; set; }
        public int LtpScaleQ14 { get; set; }
        public int RandomScaleQ14 { get; set; }
        public int RandomSeed { get; set; }
        public int SubframeSamples { get; set; }
        public int Subframes { get; set; }
        public int LossCount { get; set; }
        public SilkSignalType PreviousSignal { get; set; }
        public bool PreviousLost { get; set; }
        public int LostEnergy { get; set; }
        public int LostEnergyShift { get; set; }
        public int NoiseGainQ16 { get; set; }
        public int NoiseSeed { get; set; }
        public Span<short> Lpc => MemoryMarshal.Cast<byte, short>(storage.Span[..LtpOffset]);
        public Span<short> Ltp => MemoryMarshal.Cast<byte, short>(storage.Span.Slice(LtpOffset, 5 * sizeof(short)));
        public Span<short> NoiseLsf => MemoryMarshal.Cast<byte, short>(storage.Span.Slice(NoiseLsfOffset, 16 * sizeof(short)));
        public Span<int> NoiseExcitation => MemoryMarshal.Cast<byte, int>(storage.Span.Slice(NoiseExcitationOffset, SilkFrameMode.MaximumFrameSamples * sizeof(int)));
        public Span<int> NoiseHistory => MemoryMarshal.Cast<byte, int>(storage.Span[NoiseHistoryOffset..]);

        public void SetSampleRate(int sampleRate)
        {
            SilkFrameMode mode = new(sampleRate, 4);
            PitchQ8 = mode.FrameSamples << 7; Gain1Q16 = Gain2Q16 = 65536;
            SubframeSamples = 20; Subframes = 2; PreviousLag = 100; PreviousSignal = SilkSignalType.Inactive;
            int step = short.MaxValue / (mode.LpcOrder + 1);
            for (int at = 0; at < mode.LpcOrder; at++) { NoiseLsf[at] = (short)((at + 1) * step); }
            NoiseGainQ16 = 0; NoiseSeed = 3176576;
        }

        public void Reset(int sampleRate)
        {
            storage.Span.Clear(); SetSampleRate(sampleRate);
            LtpScaleQ14 = RandomScaleQ14 = RandomSeed = LossCount = LostEnergy = LostEnergyShift = 0;
            PreviousLost = false;
        }

        public void Dispose() => storage.Dispose();
    }
}
