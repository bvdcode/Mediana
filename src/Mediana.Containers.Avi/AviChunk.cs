// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Avi
{
    internal readonly record struct AviChunk(uint Id, long Header, uint Length)
    {
        public long Data => Header + 8;
        public long End => Data + Length;
        public long Next => End + (Length & 1);
    }
}
