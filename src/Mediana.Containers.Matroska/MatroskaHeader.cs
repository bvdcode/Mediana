// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Matroska
{
    internal readonly record struct MatroskaHeader(long BodyOffset, ulong EbmlVersion,
        ulong DocumentVersion, ulong DocumentReadVersion, int MaximumSizeWidth, string DocumentType);
}
