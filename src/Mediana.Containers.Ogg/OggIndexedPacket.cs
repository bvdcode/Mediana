// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Ogg
{
    internal readonly record struct OggIndexedPacket(long Page, long Timestamp,
        int Samples, int PresentationSamples, int Bytes, int Lace);
}
