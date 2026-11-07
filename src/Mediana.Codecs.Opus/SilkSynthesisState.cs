// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Opus
{
    internal class SilkSynthesisState : IDisposable
    {
        private const int LpcHistoryLength = 16;
        private readonly WritableMediaBuffer storage;
        private readonly int whitenedOffset, lpcOffset, ltpOffset, excitationOffset, workingOffset, residualOffset, lsfOffset;
        private int subframeSamples;
        private int lpcOrder;

        public SilkSynthesisState(int sampleRate, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            _ = new SilkFrameMode(sampleRate, 4);
            SilkFrameMode mode = new(SilkFrameMode.MaximumSampleRate, 4);
            int capacity = mode.FrameSamples;
            whitenedOffset = 2 * capacity * sizeof(short);
            lpcOffset = whitenedOffset + capacity * sizeof(short);
            ltpOffset = lpcOffset + LpcHistoryLength * sizeof(int);
            excitationOffset = ltpOffset + 2 * capacity * sizeof(int);
            workingOffset = excitationOffset + capacity * sizeof(int);
            residualOffset = workingOffset + (LpcHistoryLength + mode.SamplesPerSubframe) * sizeof(int);
            lsfOffset = residualOffset + mode.SamplesPerSubframe * sizeof(int);
            storage = allocator.Allocate(lsfOffset + LpcHistoryLength * sizeof(short));
            SetSampleRate(sampleRate);
            Reset();
        }

        public int SampleRate { get; private set; }
        public int HistorySamples { get; private set; }
        public int PreviousGainQ16 { get; set; }
        public Span<short> OutputHistory => MemoryMarshal.Cast<byte, short>(storage.Span[..(2 * HistorySamples * sizeof(short))]);
        public Span<short> Whitened => MemoryMarshal.Cast<byte, short>(storage.Span.Slice(whitenedOffset, HistorySamples * sizeof(short)));
        public Span<int> LpcHistory => Integers(lpcOffset, LpcHistoryLength);
        public Span<int> LtpHistory => Integers(ltpOffset, 2 * HistorySamples);
        public Span<int> Excitation => Integers(excitationOffset, HistorySamples);
        public Span<int> LpcWorking => Integers(workingOffset, LpcHistoryLength + subframeSamples);
        public Span<int> Residual => Integers(residualOffset, subframeSamples);
        public Span<short> PreviousLsf => MemoryMarshal.Cast<byte, short>(storage.Span.Slice(lsfOffset, lpcOrder * sizeof(short)));

        private Span<int> Integers(int offset, int count) => MemoryMarshal.Cast<byte, int>(storage.Span.Slice(offset, count * sizeof(int)));

        public void SetSampleRate(int sampleRate)
        {
            SilkFrameMode mode = new(sampleRate, 4);
            SampleRate = sampleRate; HistorySamples = mode.FrameSamples;
            subframeSamples = mode.SamplesPerSubframe; lpcOrder = mode.LpcOrder;
            ClearPrediction();
        }

        public void Reset()
        {
            storage.Span.Clear();
            PreviousGainQ16 = 65536;
        }

        public void ClearPrediction()
        {
            OutputHistory.Clear(); LpcHistory.Clear();
        }

        public void Dispose() => storage.Dispose();
    }
}
