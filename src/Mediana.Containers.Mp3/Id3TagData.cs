// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Mp3
{
    internal record Id3TagData(IReadOnlyList<Id3TextFrame> TextFrames, IReadOnlyList<Id3Picture> Pictures);
}
