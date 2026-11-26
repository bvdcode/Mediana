// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;

namespace Mediana.Codecs.Ffv1
{
    internal class Ffv1RandomAccessInspector(int streamIndex) : IRandomAccessInspector
    {
        public bool IsRandomAccess(MediaPacket packet, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(packet);
            cancellationToken.ThrowIfCancellationRequested();
            if (packet.StreamIndex != streamIndex)
            {
                throw new ArgumentException("The packet belongs to a different FFV1 stream.", nameof(packet));
            }
            Ffv1RangeReader range = new(packet.Data.Span);
            return Ffv1FrameModel.ReadKeyFrame(ref range);
        }
    }
}
