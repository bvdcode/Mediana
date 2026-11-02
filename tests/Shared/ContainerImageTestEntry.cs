// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;

namespace Mediana.Tests
{
    internal record ContainerImageTestEntry(byte[] Data, long ByteLength, PictureDataKind DataKind = PictureDataKind.Embedded,
        string? MediaType = null, string? FileName = null);
}
