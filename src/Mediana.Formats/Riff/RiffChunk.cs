// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Riff
{
    /// <summary>A borrowed RIFF chunk payload excluding its header and word padding.</summary>
    public readonly record struct RiffChunk(string Type, ReadOnlyMemory<byte> Payload);
}
