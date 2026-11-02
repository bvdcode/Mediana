// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs
{
    /// <summary>A decoder whose packets can be parsed independently, but whose correct output requires prior packets.
    /// Preroll reconstructs codec state without exposing its incomplete output.</summary>
    public interface IPacketPrerollDecoder
    {
        /// <summary>Minimum preceding encoded packets needed before the first requested output packet.</summary>
        int RequiredPrerollPackets { get; }

        /// <summary>Resets codec state and suppresses output belonging to exactly this many leading encoded packets.
        /// The caller combines codec and container requirements. Fewer packets are allowed only at the original
        /// stream beginning; ordinary startup priming is preserved. Retained output remains owned by its caller.</summary>
        void ResetForPreroll(long packetCount);
    }
}
