// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;

namespace Mediana.Codecs
{
    /// <summary>Optional restart-inspection capability for opening signaling carried in the first encoded packet.
    /// An inspector belongs to one stream; ordinary sequential decoding does not require this inspection.</summary>
    public interface IRandomAccessOriginInspector : IRandomAccessInspector
    {
        bool RequiresOriginPacket { get; }
        /// <summary>Inspects packet zero without retaining its payload. Absence of opening signaling does not
        /// establish absence throughout the stream. Cancellation or invalid syntax must be reported explicitly.</summary>
        void InspectOrigin(MediaPacket origin, CancellationToken cancellationToken = default);
    }
}
