// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal readonly record struct H264PartitionMotion(int ReferenceIndex, int X, int Y)
    {
        public static H264PartitionMotion None => new(-1, 0, 0);
    }
}
