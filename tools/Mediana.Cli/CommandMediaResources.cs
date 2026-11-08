// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Cli
{
    internal static class CommandMediaResources
    {
        internal static MediaOpenOptions OpenOptions { get; } = new()
        {
            MaxImageInputBytes = Array.MaxLength,
            BufferBudgetBytes = long.MaxValue
        };

        internal static MediaBufferAllocator CreateAllocator() => new(long.MaxValue);
    }
}
