// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Ac3
{
    internal readonly record struct Ac3FrameLocation(long Offset, long FirstSample, int ByteLength, int Samples);
}
