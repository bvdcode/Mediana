// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Dts
{
    internal class DtsRandomAccessInspector(int streamIndex) : IRandomAccessInspector, IRandomAccessOriginInspector
    {
        public bool RequiresOriginPacket => true;

        public RandomAccessRecovery? InspectOrigin(MediaPacket packet, CancellationToken cancellationToken = default)
        {
            DtsFrameHeader header = Read(packet, cancellationToken);
            return header.FrameBytes == packet.Data.Length ? new RandomAccessRecovery(0) : null;
        }

        public bool IsRandomAccess(MediaPacket packet, CancellationToken cancellationToken = default)
        {
            DtsFrameHeader header = Read(packet, cancellationToken);
            return header.FrameBytes == packet.Data.Length && !header.PredictFromPreviousFrame;
        }

        private DtsFrameHeader Read(MediaPacket packet, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(packet);
            cancellationToken.ThrowIfCancellationRequested();
            if (packet.StreamIndex != streamIndex)
            {
                throw new ArgumentException("The packet belongs to a different DTS stream.", nameof(packet));
            }
            return DtsFrameHeader.Read(packet.Data.Span);
        }
    }
}
