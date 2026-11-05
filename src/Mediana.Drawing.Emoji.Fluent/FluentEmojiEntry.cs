// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;

namespace Mediana.Drawing.Emoji.Fluent
{
    internal record FluentEmojiEntry(ZipArchiveEntry Block, int Offset, int Length, bool RequiresSelector);
}
