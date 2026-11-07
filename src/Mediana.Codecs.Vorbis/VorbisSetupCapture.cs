// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Vorbis
{
    internal class VorbisSetupCapture(VorbisIdentification identification, MediaBufferAllocator allocator) : VorbisSetupVisitor, IDisposable
    {
        private readonly List<VorbisCodebook> books = [];
        private readonly List<VorbisFloor> floors = [];
        private readonly List<VorbisResidue> residues = [];
        private readonly List<VorbisMapping> mappings = [];
        private readonly List<VorbisMode> modes = [];
        private WritableMediaBuffer? lengths, lookup;
        private int dimensions, lookupType;
        private uint minimum, delta;
        private bool sequence;

        public IReadOnlyList<VorbisCodebook> Books => books;
        public IReadOnlyList<VorbisFloor> Floors => floors;
        public IReadOnlyList<VorbisResidue> Residues => residues;
        public IReadOnlyList<VorbisMapping> Mappings => mappings;
        public IReadOnlyList<VorbisMode> Modes => modes;

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
            AddOwned(books, book);
            lengths.Dispose(); lengths = null;
            lookup?.Dispose(); lookup = null;
        }

        public override void Floor0(VorbisFloor0Definition definition, ReadOnlySpan<byte> references) =>
            AddOwned(floors, new VorbisFloor0(definition, references, identification, books, allocator));

        public override void Floor1(scoped VorbisFloor1Definition definition) =>
            AddOwned(floors, new VorbisFloor1(definition, allocator));

        public override void Residue(scoped VorbisResidueDefinition definition) =>
            AddOwned(residues, new VorbisResidue(definition, books, allocator));

        public override void Mapping(scoped VorbisMappingDefinition definition) =>
            AddOwned(mappings, new VorbisMapping(definition, allocator));

        public override void Mode(bool longBlock, int mapping) => modes.Add(new(longBlock, mapping));

        private static void AddOwned<T>(List<T> destination, T item) where T : IDisposable
        {
            bool added = false;
            try { destination.Add(item); added = true; }
            finally { if (!added) { item.Dispose(); } }
        }

        public void Dispose()
        {
            lengths?.Dispose(); lengths = null;
            lookup?.Dispose(); lookup = null;
            foreach (VorbisCodebook book in books) { book.Dispose(); }
            books.Clear();
            foreach (VorbisFloor floor in floors) { floor.Dispose(); }
            floors.Clear();
            foreach (VorbisResidue residue in residues) { residue.Dispose(); }
            residues.Clear();
            foreach (VorbisMapping mapping in mappings) { mapping.Dispose(); }
            mappings.Clear(); modes.Clear();
        }
    }
}
