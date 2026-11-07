// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Theora
{
    internal class TheoraFrameState : IDisposable
    {
        private readonly WritableMediaBuffer storage;
        private readonly int blocks, macros, supers;
        internal TheoraFrameState(TheoraLayout layout, MediaBufferAllocator allocator)
        {
            blocks = layout.BlockCount; macros = layout.MacroCount; supers = layout.SuperCount;
            storage = allocator.Allocate(checked(blocks * 140 + ((macros + supers * 2 + 3) & ~3)));
        }
        internal Span<short> Coefficients => MemoryMarshal.Cast<byte, short>(storage.Span[..(blocks * 128)]);
        internal Span<int> CodedBlocks => MemoryMarshal.Cast<byte, int>(storage.Span.Slice(blocks * 128, blocks * 4));
        internal Span<byte> Coded => storage.Span.Slice(blocks * 132, blocks);
        internal Span<byte> TokenIndices => storage.Span.Slice(blocks * 133, blocks);
        internal Span<byte> CoefficientCounts => storage.Span.Slice(blocks * 134, blocks);
        internal Span<byte> QualityIndices => storage.Span.Slice(blocks * 135, blocks);
        internal Span<sbyte> MotionX => MemoryMarshal.Cast<byte, sbyte>(storage.Span.Slice(blocks * 136, blocks));
        internal Span<sbyte> MotionY => MemoryMarshal.Cast<byte, sbyte>(storage.Span.Slice(blocks * 137, blocks));
        internal Span<byte> Bits => storage.Span.Slice(blocks * 138, blocks);
        internal Span<byte> Modes => storage.Span.Slice(blocks * 140, macros);
        internal Span<byte> PartialSupers => storage.Span.Slice(blocks * 140 + macros, supers);
        internal Span<byte> FullSupers => storage.Span.Slice(blocks * 140 + macros + supers, supers);
        internal int CodedCount { get; set; }
        internal void Clear() { storage.Span.Clear(); CodedCount = 0; }
        public void Dispose() => storage.Dispose();
    }
}
