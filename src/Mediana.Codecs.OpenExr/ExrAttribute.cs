// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.OpenExr
{
    internal readonly record struct ExrAttribute(string Type, ReadOnlyMemory<byte> Data);
}
