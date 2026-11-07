// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;

namespace Mediana.Containers.Mp4
{
    /// <summary>Pairs a track declaration with borrowed decode-order packets. Empty tracks are omitted from a fragment.</summary>
    public class Mp4TrackPackets(Mp4TrackDefinition track, IReadOnlyList<MediaPacket> packets)
    {
        public Mp4TrackDefinition Track { get; } = track ?? throw new ArgumentNullException(nameof(track));
        public IReadOnlyList<MediaPacket> Packets { get; } = packets ?? throw new ArgumentNullException(nameof(packets));
    }
}
