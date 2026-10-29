// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Core.Memory
{
    public class MediaMemoryLimitException(long requestedBytes, long limitBytes)
        : InvalidOperationException($"Cannot reserve {requestedBytes} bytes within the {limitBytes}-byte media buffer budget.")
    {
        public long RequestedBytes { get; } = requestedBytes;
        public long LimitBytes { get; } = limitBytes;
    }
}
