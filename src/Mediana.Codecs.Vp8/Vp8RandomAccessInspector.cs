// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;

namespace Mediana.Codecs.Vp8
{
    internal class Vp8RandomAccessInspector(int streamIndex) : IRandomAccessInspector
    {
        public bool IsRandomAccess(MediaPacket packet, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(packet);
            cancellationToken.ThrowIfCancellationRequested();
            if (packet.StreamIndex != streamIndex) { throw new ArgumentException("The packet belongs to a different VP8 stream.", nameof(packet)); }
            return Vp8UncompressedHeader.Parse(packet.Data.Span).KeyFrame;
        }
    }
}
