// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Formats.Video;

namespace Mediana.Codecs.Mpeg4Visual
{
    internal class Mpeg4RandomAccessInspector(EncodedStreamInfo stream) : IRandomAccessInspector
    {
        private Mpeg4VisualConfiguration? configuration = stream.Configuration.IsEmpty ? null : Mpeg4VisualConfiguration.Read(stream.Configuration.Span);
        public bool IsRandomAccess(MediaPacket packet, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(packet); cancellationToken.ThrowIfCancellationRequested();
            if (packet.StreamIndex != stream.StreamIndex) { throw new ArgumentException("The packet belongs to a different MPEG-4 Visual stream.", nameof(packet)); }
            ReadOnlySpan<byte> data = packet.Data.Span;
            for (int at = Mpeg4VisualStartCodes.Find(data); at >= 0; at = Mpeg4VisualStartCodes.Find(data, at + 4))
            {
                if (data[at + 3] is >= 0x20 and <= 0x2f) { configuration = Mpeg4VisualConfiguration.Read(data[at..]); }
                if (data[at + 3] != 0xb6) { continue; }
                if (configuration is null) { throw new InvalidDataException("MPEG-4 Visual random-access inspection requires an object-layer header."); }
                Mpeg4VisualBitReader bits = new(data[(at + 4)..]); Mpeg4VopHeader header = Mpeg4VopHeader.Read(ref bits, configuration);
                return header.IsCoded && header.Type == Mpeg4VopType.Intra;
            }
            return false;
        }
    }
}
