// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;

namespace Mediana.Codecs.Vp9
{
    internal class Vp9RandomAccessInspector(int streamIndex) : IRandomAccessInspector
    {
        public bool IsRandomAccess(MediaPacket packet, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(packet); cancellationToken.ThrowIfCancellationRequested();
            if (packet.StreamIndex != streamIndex) { throw new ArgumentException("The packet belongs to a different VP9 stream.", nameof(packet)); }
            Span<Range> ranges = stackalloc Range[8]; Vp9Superframe.Parse(packet.Data.Span, ranges);
            ReadOnlySpan<byte> first = packet.Data.Span[ranges[0]];
            Vp9BitReader bits = new(first);
            if (bits.Read(2) != 2) { throw new InvalidDataException("Invalid VP9 frame marker."); }
            int profile = bits.Read(1) | bits.Read(1) << 1;
            if (profile == 3 && bits.Flag()) { throw new InvalidDataException("A VP9 reserved bit must be zero."); }
            if (bits.Flag() || bits.Flag()) { return false; }
            return Vp9FrameHeader.Parse(first, new()).KeyFrame;
        }
    }
}
