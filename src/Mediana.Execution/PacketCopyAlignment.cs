// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Execution
{
    /// <summary>Controls how a presentation interval owns compressed packets without changing their timestamps or payload.</summary>
    public enum PacketCopyAlignment
    {
        Exact,
        PresentationStarts
    }
}
