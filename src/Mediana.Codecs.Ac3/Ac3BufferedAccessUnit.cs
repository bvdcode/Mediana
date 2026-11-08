// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Codecs.Ac3
{
    internal struct Ac3BufferedAccessUnit : IDisposable
    {
        public WritableMediaBuffer? Writable;
        public MediaBuffer? Published;
        public long Start;
        public int CodedSamples, PresentationSamples;
        public MediaTimestamp? Timestamp;
        public bool Delivered;
        public readonly long End => Start + CodedSamples;
        public readonly ReadOnlySpan<float> Samples => Writable is not null ? MemoryMarshal.Cast<byte, float>(Writable.Span) : MemoryMarshal.Cast<byte, float>(Published!.Memory.Span);
        public readonly Span<float> MutableSamples => MemoryMarshal.Cast<byte, float>((Writable ?? throw new InvalidOperationException("Published AC-3 PCM cannot be modified.")).Span);
        public void Dispose() { Writable?.Dispose(); Published?.Dispose(); this = default; }
    }
}
