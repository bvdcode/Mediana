// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Word
{
    internal class WordTextContainer(DocumentTextNodeKind kind, int index, int depth)
    {
        internal DocumentTextPathSegment Segment { get; } = new(kind, index);
        internal int Depth { get; } = depth;
        internal int NextIndex { get; set; }
    }
}
