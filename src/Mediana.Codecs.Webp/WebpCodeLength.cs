// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Webp
{
    internal readonly record struct WebpCodeLength(int Symbol, uint Extra = 0, int ExtraBits = 0);
}
