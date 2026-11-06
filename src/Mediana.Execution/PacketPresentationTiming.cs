// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Timing;

namespace Mediana.Execution
{
    internal static class PacketPresentationTiming
    {
        /// <summary>Consumes the packet lease and returns its presentation-timed replacement without copying payload.</summary>
        public static MediaPacket Apply(MediaPacket packet, MediaDuration? duration)
        {
            bool retained = false;
            try
            {
                if (packet.Duration == duration)
                {
                    retained = true;
                    return packet;
                }
                return packet.RetainWithDuration(duration);
            }
            finally
            {
                if (!retained)
                {
                    packet.Dispose();
                }
            }
        }
    }
}
