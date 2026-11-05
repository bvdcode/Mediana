// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Word
{
    internal record WordBorders(WordBorder? Left = null, WordBorder? Right = null, WordBorder? Top = null, WordBorder? Bottom = null);
}
