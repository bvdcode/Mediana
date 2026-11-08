// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;

namespace Mediana.Codecs
{
    /// <summary>Validates a packet's format-defined integrity side data without reconstructing media samples.
    /// Invalid or truncated protection data throws; absent protection is reported explicitly.</summary>
    public interface IPacketIntegrityValidator
    {
        string SideDataKind { get; }
        PacketIntegrityStatus Validate(MediaPacket packet, CancellationToken cancellationToken = default);
    }
}
