// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Mp4
{
    /// <summary>A recognized name or tnam user-data atom, distinct from a typed metadata data atom.
    /// ByteLength counts its complete payload, including language and string framing when present.</summary>
    public record Mp4UserDataItem(string Name, int ByteLength, string Text, ushort? LanguageCode,
        string? Language, string SourcePath);
}
