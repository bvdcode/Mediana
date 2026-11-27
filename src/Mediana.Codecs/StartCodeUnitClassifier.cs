// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs
{
    internal delegate StartCodeUnitKind StartCodeUnitClassifier(ReadOnlySpan<byte> unit);
}
