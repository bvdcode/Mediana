// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Flac
{
    /// <summary>Original Vorbis comment spelling and value. Compare names with OrdinalIgnoreCase.
    /// Repeated names retain their file order.</summary>
    public record FlacComment(string Name, string Value);
}
