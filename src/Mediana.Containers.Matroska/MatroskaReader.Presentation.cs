// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Containers.Matroska
{
    public partial class MatroskaReader
    {
        public long GetPresentationUnitCount(int streamIndex)
        {
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                return packets.GetPresentationUnitCount(streamIndex);
            }
            finally { Volatile.Write(ref busy, 0); }
        }

        public ContainerVideoPresentationUnit GetPresentationUnit(int streamIndex, long presentationUnitIndex,
            CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                return packets.GetPresentationUnit(streamIndex, presentationUnitIndex, cancellationToken);
            }
            finally { Volatile.Write(ref busy, 0); }
        }

        public MediaDuration? GetPresentationDuration(int streamIndex, long packetIndex, CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                return packets.GetPresentationDuration(streamIndex, packetIndex, cancellationToken);
            }
            finally { Volatile.Write(ref busy, 0); }
        }
    }
}
