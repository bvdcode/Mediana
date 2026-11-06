// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Xls
{
    internal record CompoundEntry(string Name, byte Type, uint Left, uint Right, uint Child, uint Start, long Length);
}
