// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Audio
{
    internal abstract class VorbisSetupVisitor
    {
        public abstract Span<byte> BeginCodebook(int dimensions, int entries);
        public abstract Span<ushort> BeginLookup(int type, uint minimum, uint delta, bool sequence, int values);
        public abstract void EndCodebook(CancellationToken token);
        public virtual void Floor0(VorbisFloor0Definition definition, ReadOnlySpan<byte> books) { }
        public virtual void Floor1(scoped VorbisFloor1Definition definition) { }
        public virtual void Residue(scoped VorbisResidueDefinition definition) { }
        public virtual void Mapping(scoped VorbisMappingDefinition definition) { }
        public virtual void Mode(bool longBlock, int mapping) { }
    }
}
