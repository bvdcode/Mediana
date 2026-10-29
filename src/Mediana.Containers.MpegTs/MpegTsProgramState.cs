// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.MpegTs
{
    internal readonly record struct MpegTsProgramState(long LastDecodeTime, long? LastPcr, long? LastTables,
        int PatCounter, int PmtCounter);
}
