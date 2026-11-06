// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal readonly ref struct SilkParameterBuffers(Span<int> gainsQ16, Span<short> lpcQ12,
        Span<int> pitchLags, Span<short> ltpQ14)
    {
        public Span<int> GainsQ16 { get; } = gainsQ16;
        public Span<short> LpcQ12 { get; } = lpcQ12;
        public Span<int> PitchLags { get; } = pitchLags;
        public Span<short> LtpQ14 { get; } = ltpQ14;
    }
}
