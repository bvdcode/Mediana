// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.IO
{
    /// <summary>A seekable read-only view over borrowed memory. The memory must remain valid until disposal.</summary>
    public class ReadOnlyMemoryStream(ReadOnlyMemory<byte> memory) : BorrowedMemoryStream(memory)
    {
    }
}
