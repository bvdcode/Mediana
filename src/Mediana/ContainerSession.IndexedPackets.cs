// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Timing;

namespace Mediana
{
    public partial class ContainerSession
    {
        public bool CanReadPacketsAt => Reader is IIndexedContainerReader;

        /// <summary>Returns the selected stream's indexed packet count without reading media payload.</summary>
        public long GetPacketCount(int streamIndex)
        {
            Enter();
            try
            {
                RequireActive();
                return RequireIndex().GetPacketCount(streamIndex);
            }
            finally { Volatile.Write(ref busy, 0); }
        }

        /// <summary>Returns the container's optional explicit presentation interval on its packet timeline.
        /// Does not infer delay or padding and does not advance packet cursors.</summary>
        public MediaTimeRange? GetPresentationRange(int streamIndex, CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                RequireActive();
                ArgumentOutOfRangeException.ThrowIfNegative(streamIndex);
                if (!Streams.Any(stream => stream.StreamIndex == streamIndex))
                {
                    throw new ArgumentOutOfRangeException(nameof(streamIndex));
                }
                cancellationToken.ThrowIfCancellationRequested();
                return (Reader as IContainerPresentationReader)?.GetPresentationRange(streamIndex, cancellationToken);
            }
            finally { Volatile.Write(ref busy, 0); }
        }

        /// <summary>Reads an indexed packet without moving sequential cursors. Counts against the shared packet-read budget.
        /// Returned packets survive session disposal. A started read failure faults the session;
        /// unsupported indexing or invalid indices leave it usable. Packet reads prevent decode-session transfer.</summary>
        public async ValueTask<MediaPacket> ReadPacketAtAsync(int streamIndex, long packetIndex,
            CancellationToken cancellationToken = default)
        {
            Enter();
            bool started = false;
            bool succeeded = false;
            try
            {
                RequireActive();
                IIndexedContainerReader indexed = RequireIndex();
                ArgumentOutOfRangeException.ThrowIfNegative(packetIndex);
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(packetIndex, indexed.GetPacketCount(streamIndex));
                started = true;
                cancellationToken.ThrowIfCancellationRequested();
                CountPacketRead();
                packetsStarted = true;
                MediaPacket packet = await indexed.ReadPacketAtAsync(streamIndex, packetIndex, cancellationToken).ConfigureAwait(false);
                succeeded = true;
                return packet;
            }
            finally
            {
                if (started && !succeeded) { faulted = true; }
                Volatile.Write(ref busy, 0);
            }
        }

        private IIndexedContainerReader RequireIndex() => Reader as IIndexedContainerReader
            ?? throw new NotSupportedException("The container reader does not expose indexed packets.");
    }
}
