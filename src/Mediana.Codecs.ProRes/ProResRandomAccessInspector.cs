// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Formats.Video;

namespace Mediana.Codecs.ProRes
{
    internal class ProResRandomAccessInspector(int streamIndex) : IRandomAccessInspector
    {
        public bool IsRandomAccess(MediaPacket packet, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(packet);
            cancellationToken.ThrowIfCancellationRequested();
            if (packet.StreamIndex != streamIndex)
            {
                throw new ArgumentException("The packet belongs to a different ProRes stream.", nameof(packet));
            }
            ProResFrameHeader header = ProResFrameHeader.Read(packet.Data);
            return header.FrameBytes == packet.Data.Length;
        }
    }
}
