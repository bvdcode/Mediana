// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Formats.Video;

namespace Mediana.Codecs.Dv
{
    internal class DvRandomAccessInspector(int streamIndex) : IRandomAccessInspector
    {
        public bool IsRandomAccess(MediaPacket packet, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(packet);
            cancellationToken.ThrowIfCancellationRequested();
            if (packet.StreamIndex != streamIndex)
            {
                throw new ArgumentException("The packet belongs to a different DV stream.", nameof(packet));
            }
            DvFrameHeader.Read(packet.Data.Span);
            return true;
        }
    }
}
