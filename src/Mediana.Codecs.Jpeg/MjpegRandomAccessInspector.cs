// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;

namespace Mediana.Codecs.Jpeg
{
    internal class MjpegRandomAccessInspector(int streamIndex, JpegDecodeOptions options) : IRandomAccessInspector
    {
        public bool IsRandomAccess(MediaPacket packet, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(packet);
            cancellationToken.ThrowIfCancellationRequested();
            if (packet.StreamIndex != streamIndex) { throw new ArgumentException("The packet belongs to a different MJPEG stream.", nameof(packet)); }
            ReadOnlySpan<byte> bytes = packet.Data.Span;
            if (bytes.Length < 4 || bytes[0] != 255 || bytes[1] != 216 || bytes[^2] != 255 || bytes[^1] != 217)
            { return false; }
            using JpegDocument image = new(packet.Data, options, null, null, cancellationToken);
            image.Read(null, componentSamples: true);
            return image.Components.Count is 1 or 3;
        }
    }
}
