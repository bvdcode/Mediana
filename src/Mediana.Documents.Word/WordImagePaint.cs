// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Word
{
    internal record WordImagePaint(WordPicture Picture, double X, double Y) : WordPaint;
}
