// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Hevc
{
    internal readonly record struct HevcReferenceEntry(HevcReferencePicture Picture, bool LongTerm);
}
