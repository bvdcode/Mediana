// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Vorbis
{
    internal class VorbisSetupCapture(MediaBufferAllocator allocator) : VorbisSetupVisitor, IDisposable
    {
        private readonly List<VorbisCodebook> books = [];
        private WritableMediaBuffer? lengths, lookup;
        private int dimensions, lookupType;
        private uint minimum, delta;
        private bool sequence;

        public IReadOnlyList<VorbisCodebook> Books => books;

        public override Span<byte> BeginCodebook(int dimensions, int entries)
        {
            this.dimensions = dimensions;
            lookupType = 0; minimum = 0; delta = 0; sequence = false;
            lengths = allocator.Allocate(entries);
            return lengths.Span;
        }

        public override Span<ushort> BeginLookup(int type, uint minimum, uint delta, bool sequence, int values)
        {
            lookupType = type;
            this.minimum = minimum;
            this.delta = delta;
            this.sequence = sequence;
            lookup = allocator.Allocate(checked(values * sizeof(ushort)));
            return MemoryMarshal.Cast<byte, ushort>(lookup.Span);
        }

        public override void EndCodebook(CancellationToken token)
        {
            ReadOnlySpan<ushort> values = lookup is null ? [] : MemoryMarshal.Cast<byte, ushort>(lookup.Span);
            VorbisCodebook book = new(dimensions, lengths!.Span, lookupType, minimum, delta, sequence, values, allocator, token);
            bool added = false;
            try { books.Add(book); added = true; }
            finally { if (!added) { book.Dispose(); } }
            lengths.Dispose(); lengths = null;
            lookup?.Dispose(); lookup = null;
        }

        public void Dispose()
        {
            lengths?.Dispose(); lengths = null;
            lookup?.Dispose(); lookup = null;
            foreach (VorbisCodebook book in books) { book.Dispose(); }
            books.Clear();
        }
    }
}
