// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Mpeg2
{
    internal readonly record struct Mpeg2Codeword(int Code, int Length, int Value);
}
