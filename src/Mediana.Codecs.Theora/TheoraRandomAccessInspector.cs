// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;

namespace Mediana.Codecs.Theora
{
    internal class TheoraRandomAccessInspector(int streamIndex) : IRandomAccessInspector
    {
        public bool IsRandomAccess(MediaPacket packet, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(packet); cancellationToken.ThrowIfCancellationRequested();
            if (packet.StreamIndex != streamIndex) { throw new ArgumentException("The packet belongs to a different Theora stream.", nameof(packet)); }
            if (packet.Data.IsEmpty) { return false; }
            TheoraBitReader reader = new(packet.Data.Span); Span<int> qualities = stackalloc int[3];
            return !TheoraFrameHeader.Read(ref reader, qualities, out _);
        }
    }
}
