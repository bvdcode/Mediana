// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Matroska
{
    public partial class MatroskaReader : IIndexedPacketPrefixReader
    {
        public async ValueTask<int> ReadPacketPrefixAtAsync(int streamIndex, long packetIndex, Memory<byte> destination,
            CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                return await packets.ReadPrefixAtAsync(streamIndex, packetIndex, destination, cancellationToken).ConfigureAwait(false);
            }
            finally { Volatile.Write(ref busy, 0); }
        }
    }
}
