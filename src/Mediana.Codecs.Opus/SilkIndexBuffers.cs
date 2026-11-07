// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal readonly ref struct SilkIndexBuffers(Span<byte> gains, Span<sbyte> lsf, Span<byte> pitchFilters)
    {
        public Span<byte> Gains { get; } = gains;
        public Span<sbyte> Lsf { get; } = lsf;
        public Span<byte> PitchFilters { get; } = pitchFilters;
    }
}
