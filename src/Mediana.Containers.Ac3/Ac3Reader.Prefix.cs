// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.IO;

namespace Mediana.Containers.Ac3
{
    public partial class Ac3Reader : IIndexedPacketPrefixReader
    {
        public async ValueTask<int> ReadPacketPrefixAtAsync(int streamIndex, long packetIndex, Memory<byte> destination,
            CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                Ac3FrameLocation frame = locations[RequirePacket(streamIndex, packetIndex)];
                cancellationToken.ThrowIfCancellationRequested();
                int count = Math.Min(destination.Length, frame.ByteLength);
                await source.ReadExactlyAtAsync(frame.Offset, destination[..count], cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return count;
            }
            finally { Exit(); }
        }
    }
}
