// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Metadata;

namespace Mediana.Containers.Matroska
{
    internal record MatroskaSegmentMetadata(MatroskaSegmentInfo Info, IReadOnlyList<MediaTag> Tags);
}
