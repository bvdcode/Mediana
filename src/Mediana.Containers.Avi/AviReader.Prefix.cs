// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Avi
{
    public partial class AviReader
    {
        public async ValueTask<int> ReadPacketPrefixAtAsync(int streamIndex, long packetIndex, Memory<byte> destination,
            CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                EnsureReadable();
                cancellationToken.ThrowIfCancellationRequested();
                AviTrack track = Track(streamIndex);
                AviPacketEntry entry = track.Packets[packetIndex];
                int size = Math.Min(destination.Length, entry.Length);
                if (size != 0)
                {
                    if (entry.SourceChunkIndex < 0)
                    {
                        await ValidatePacketExtentAsync(entry, cancellationToken).ConfigureAwait(false);
                    }
                    await ReadPacketBytesAsync(track, entry, destination[..size], cancellationToken).ConfigureAwait(false);
                }
                return size;
            }
            finally { Volatile.Write(ref busy, 0); }
        }
    }
}
