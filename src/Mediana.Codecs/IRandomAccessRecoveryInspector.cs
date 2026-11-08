// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;

namespace Mediana.Codecs
{
    /// <summary>Inspects a restart candidate and its codec-specific output recovery requirement in one pass.</summary>
    public interface IRandomAccessRecoveryInspector : IRandomAccessInspector
    {
        /// <summary>Returns null when the packet is not a restart point. The result does not establish compressed
        /// payload validity or replace a larger container-declared recovery distance.</summary>
        RandomAccessRecovery? InspectRecovery(MediaPacket packet, CancellationToken cancellationToken = default);
    }
}
