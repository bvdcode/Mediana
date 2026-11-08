// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Core
{
    /// <summary>A borrowed view whose data must not outlive the packet that returned it.</summary>
    public readonly record struct MediaPacketSideDataView(string Kind, ReadOnlyMemory<byte> Data);
}
