// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Vp9
{
    internal struct Vp9BlockInfo
    {
        internal uint SubModes;
        internal byte Size, Transform, Segment, Mode;
        internal bool Skip;
        internal readonly int SubMode(int index) => (int)(SubModes >> (index * 8)) & 255;
    }
}
