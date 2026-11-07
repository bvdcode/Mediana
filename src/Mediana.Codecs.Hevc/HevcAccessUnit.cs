// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Hevc
{
    internal record HevcAccessUnit(HevcConfiguration Configuration, IReadOnlyList<HevcSliceHeader> Slices, bool EndSequence, bool EndStream);
}
