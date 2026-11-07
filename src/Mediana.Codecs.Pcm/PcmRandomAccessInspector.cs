// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;

namespace Mediana.Codecs.Pcm
{
    internal class PcmRandomAccessInspector(int streamIndex, int blockAlignment) : IRandomAccessInspector
    {
        public bool IsRandomAccess(MediaPacket packet, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(packet);
            cancellationToken.ThrowIfCancellationRequested();
            if (packet.StreamIndex != streamIndex) { throw new ArgumentException("The packet belongs to a different PCM stream.", nameof(packet)); }
            return packet.Data.Length != 0 && packet.Data.Length % blockAlignment == 0;
        }
    }
}
