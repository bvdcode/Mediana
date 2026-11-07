// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal readonly ref struct CeltSpectralBuffers(Span<float> coefficients, Span<float> folding, Span<byte> collapseMasks)
    {
        public Span<float> Coefficients { get; } = coefficients;
        public Span<float> Folding { get; } = folding;
        public Span<byte> CollapseMasks { get; } = collapseMasks;
    }
}
