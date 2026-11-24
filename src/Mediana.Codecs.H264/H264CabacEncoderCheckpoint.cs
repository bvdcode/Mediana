// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal readonly record struct H264CabacEncoderCheckpoint(H264BitWriterCheckpoint Writer, int Range, ulong Low,
        int PendingBits, uint HeldWord, bool HasHeldWord, long OutstandingWords, bool Terminated, long BinCount);
}
