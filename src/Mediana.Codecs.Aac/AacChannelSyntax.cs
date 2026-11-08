// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Aac
{
    internal readonly ref struct AacChannelSyntax(AacWindowInfo info, Span<byte> books, Span<int> scales,
        Span<int> quantized, List<AacTnsFilter>? filters)
    {
        public AacWindowInfo Info { get; } = info;
        public Span<byte> Books { get; } = books;
        public Span<int> Scales { get; } = scales;
        public Span<int> Quantized { get; } = quantized;
        public List<AacTnsFilter>? Filters { get; } = filters;
    }
}
