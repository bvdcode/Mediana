// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;

namespace Mediana.Codecs
{
    /// <summary>Validates codec restart headers using the original stream configuration and this packet.
    /// Does not depend on prior packets or claim that compressed payload data is valid.</summary>
    public interface IRandomAccessInspector
    {
        bool IsRandomAccess(MediaPacket packet, CancellationToken cancellationToken = default);
    }
}
