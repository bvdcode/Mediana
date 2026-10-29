// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Containers.Flac
{
    internal record FlacPicture(FlacPictureInfo Info, MediaBuffer Block, int DataOffset);
}
