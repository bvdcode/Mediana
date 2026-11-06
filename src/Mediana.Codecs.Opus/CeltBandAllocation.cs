// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal readonly ref struct CeltBandAllocation
    {
        public CeltBandAllocation(Span<int> shapeEighthBits, Span<int> fineBits, Span<byte> priorities)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(shapeEighthBits.Length, CeltBandTables.BandCount, nameof(shapeEighthBits));
            ArgumentOutOfRangeException.ThrowIfLessThan(fineBits.Length, CeltBandTables.BandCount, nameof(fineBits));
            ArgumentOutOfRangeException.ThrowIfLessThan(priorities.Length, CeltBandTables.BandCount, nameof(priorities));
            ShapeEighthBits = shapeEighthBits;
            FineBits = fineBits;
            Priorities = priorities;
        }

        public Span<int> ShapeEighthBits { get; }
        public Span<int> FineBits { get; }
        public Span<byte> Priorities { get; }
    }
}
