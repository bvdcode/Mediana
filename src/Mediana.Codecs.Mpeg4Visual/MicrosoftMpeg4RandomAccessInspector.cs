// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Formats.Video;

namespace Mediana.Codecs.Mpeg4Visual
{
    internal class MicrosoftMpeg4RandomAccessInspector(int streamIndex, bool wmv2) : IRandomAccessInspector
    {
        public bool IsRandomAccess(MediaPacket packet, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(packet);
            cancellationToken.ThrowIfCancellationRequested();
            if (packet.StreamIndex != streamIndex)
            {
                throw new ArgumentException("The packet belongs to a different Microsoft MPEG-4 stream.", nameof(packet));
            }
            Mpeg4VisualBitReader bits = new(packet.Data.Span);
            return MicrosoftMpeg4PictureDecoder.ReadIntraFlag(ref bits, wmv2);
        }
    }
}
