// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents
{
    /// <summary>A zero-based position within a structural parent. Sheet row/cell indexes retain sparse source coordinates.</summary>
    public readonly record struct DocumentTextPathSegment(DocumentTextNodeKind Kind, int Index);
}
