// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Word
{
    internal record WordPicture(string PartName, ReadOnlyMemory<byte> Encoded, double Width, double Height, WordRunFormat Style) : WordInline(Style);
}
