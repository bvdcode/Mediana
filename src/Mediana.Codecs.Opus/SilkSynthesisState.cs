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
        private readonly int whitenedOffset, lpcOffset, ltpOffset, excitationOffset, workingOffset, residualOffset;
        private readonly int subframeSamples;

        public SilkSynthesisState(int sampleRate, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            SilkFrameMode mode = new(sampleRate, 4);
            SampleRate = sampleRate; HistorySamples = mode.FrameSamples; subframeSamples = mode.SamplesPerSubframe;
            whitenedOffset = 2 * HistorySamples * sizeof(short);
            lpcOffset = whitenedOffset + HistorySamples * sizeof(short);
            ltpOffset = lpcOffset + LpcHistoryLength * sizeof(int);
            excitationOffset = ltpOffset + 2 * HistorySamples * sizeof(int);
            workingOffset = excitationOffset + HistorySamples * sizeof(int);
            residualOffset = workingOffset + (LpcHistoryLength + subframeSamples) * sizeof(int);
            storage = allocator.Allocate(residualOffset + subframeSamples * sizeof(int));
            Reset();
        }

        public int SampleRate { get; }
        public int HistorySamples { get; }
        public int PreviousGainQ16 { get; set; }
        public Span<short> OutputHistory => MemoryMarshal.Cast<byte, short>(storage.Span[..whitenedOffset]);
        public Span<short> Whitened => MemoryMarshal.Cast<byte, short>(storage.Span.Slice(whitenedOffset, HistorySamples * sizeof(short)));
        public Span<int> LpcHistory => Integers(lpcOffset, LpcHistoryLength);
        public Span<int> LtpHistory => Integers(ltpOffset, 2 * HistorySamples);
        public Span<int> Excitation => Integers(excitationOffset, HistorySamples);
        public Span<int> LpcWorking => Integers(workingOffset, LpcHistoryLength + subframeSamples);
        public Span<int> Residual => Integers(residualOffset, subframeSamples);

        private Span<int> Integers(int offset, int count) => MemoryMarshal.Cast<byte, int>(storage.Span.Slice(offset, count * sizeof(int)));

        public void Reset()
        {
            storage.Span.Clear();
            PreviousGainQ16 = 65536;
        }

        public void Dispose() => storage.Dispose();
    }
}
