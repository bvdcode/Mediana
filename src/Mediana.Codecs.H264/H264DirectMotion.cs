// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal readonly record struct H264DirectMotion(H264PartitionMotion L0, H264PartitionMotion L1);
}
