// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Webp
{
    internal readonly record struct WebpChunk(string Type, ReadOnlyMemory<byte> Payload);
}
