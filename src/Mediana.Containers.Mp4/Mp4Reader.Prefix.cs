// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.IO;

namespace Mediana.Containers.Mp4
{
    public partial class Mp4Reader : IIndexedPacketPrefixReader
    {
        /// <summary>Reads only the requested prefix of an indexed sample without moving sequential cursors.</summary>
        public async ValueTask<int> ReadPacketPrefixAtAsync(int streamIndex, long packetIndex, Memory<byte> destination,
            CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                ArgumentOutOfRangeException.ThrowIfNegative(packetIndex);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(packetIndex, int.MaxValue);
                Mp4SampleLocation location = Locate(streamIndex, (int)packetIndex, cancellationToken);
                int length = Math.Min(destination.Length, location.Size);
                await source.ReadExactlyAtAsync(location.Offset, destination[..length], cancellationToken).ConfigureAwait(false);
                return length;
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }
    }
}
