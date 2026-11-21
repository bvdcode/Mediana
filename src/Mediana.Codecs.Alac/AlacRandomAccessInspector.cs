// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;

namespace Mediana.Codecs.Alac
{
    internal class AlacRandomAccessInspector(int streamIndex) : IRandomAccessInspector
    {
        public bool IsRandomAccess(MediaPacket packet, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(packet);
            cancellationToken.ThrowIfCancellationRequested();
            if (packet.StreamIndex != streamIndex)
            {
                throw new ArgumentException("The packet belongs to a different ALAC stream.", nameof(packet));
            }
            return packet.Data.Length >= 3 && (packet.Data.Span[0] >> 5) is 0 or 1 or 3 or 4 or 6;
        }
    }
}
