// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Opus
{
    internal class CeltRecoveryState : IDisposable
    {
        private const int BackgroundCount = 2 * CeltBandTables.BandCount;
        private readonly WritableMediaBuffer storage;

        public CeltRecoveryState(MediaBufferAllocator allocator)
        {
            storage = allocator.Allocate((BackgroundCount + 2 * CeltLpcAnalysis.Order) * sizeof(float));
            Reset();
        }

        public int LostDuration { get; set; }
        public int Pitch { get; set; }
        public bool PreviousPeriodic { get; set; }
        public bool SkipPitch { get; set; }
        public bool FoldOverlap { get; set; }
        public Span<float> Background => MemoryMarshal.Cast<byte, float>(storage.Span)[..BackgroundCount];
        public Span<float> Coefficients => MemoryMarshal.Cast<byte, float>(storage.Span)[BackgroundCount..];

        public void Receive(CeltBandMode mode, ReadOnlySpan<float> energies)
        {
            float increase = Math.Min(160, LostDuration + (1 << mode.FrameShift)) * .001f;
            for (int at = 0; at < BackgroundCount; at++) { Background[at] = Math.Min(Background[at] + increase, energies[at]); }
            LostDuration = 0; PreviousPeriodic = false; FoldOverlap = false;
        }

        public void Reset()
        {
            storage.Span.Clear(); LostDuration = Pitch = 0;
            PreviousPeriodic = FoldOverlap = false; SkipPitch = true;
        }

        public void Dispose() => storage.Dispose();
    }
}
