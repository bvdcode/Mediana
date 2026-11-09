// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Metadata;

namespace Mediana.Containers.Matroska
{
    public partial class MatroskaReader : IContainerStreamMetadataProvider
    {
        public StreamMetadata? GetStreamMetadata(int streamIndex)
        {
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this); index.GetPacketCount(streamIndex);
                return packets.GetStreamMetadata(streamIndex);
            }
            finally { Volatile.Write(ref busy, 0); }
        }
    }
}
