// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Adts
{
    public class AdtsReaderOptions
    {
        /// <summary>Transport framing preserves every block in an ADTS frame. Raw projection requires exactly one block.</summary>
        public AdtsPacketFraming PacketFraming { get; init; } = AdtsPacketFraming.TransportFrame;

        internal void Validate()
        {
            if (!Enum.IsDefined(PacketFraming))
            {
                throw new ArgumentOutOfRangeException(nameof(PacketFraming));
            }
        }
    }
}
