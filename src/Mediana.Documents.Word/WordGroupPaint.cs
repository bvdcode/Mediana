// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Word
{
    internal record WordGroupPaint(IReadOnlyList<WordPaint> Children, double X, double Y, double? ClipWidth = null, double? ClipHeight = null) : WordPaint;
}
