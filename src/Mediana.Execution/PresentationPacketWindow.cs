// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Timing;

namespace Mediana.Execution
{
    /// <summary>Owns an encoded source whose presentation times are nondecreasing.
    /// Transfers whole packets starting inside a half-open interval, without copying payloads.
    /// Reads at most the first packet after the interval; does not drain later input.</summary>
    public class PresentationPacketWindow : IEncodedPacketSource
    {
        private readonly IEncodedPacketSource source;
        private readonly MediaTimeRange selection;
        private MediaTimestamp? previous;
        private bool ended;
        private bool disposed;
        private bool faulted;
        private int busy;

        public PresentationPacketWindow(IEncodedPacketSource source, MediaTimeRange selection)
        {
            ArgumentNullException.ThrowIfNull(source);
            selection.Start.TimeBase.Validate(); selection.End.TimeBase.Validate();
            this.source = source;
            this.selection = selection;
            ended = selection.Start == selection.End;
        }

        public EncodedStreamInfo Stream => source.Stream;

        public async ValueTask<MediaPacket?> ReadPacketAsync(CancellationToken cancellationToken = default)
        {
            Enter();
            bool success = false;
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (faulted) { throw new InvalidOperationException("The packet window has failed and must be disposed."); }
                cancellationToken.ThrowIfCancellationRequested();
                while (!ended)
                {
                    MediaPacket? packet = await source.ReadPacketAsync(cancellationToken).ConfigureAwait(false);
                    if (packet is null) { ended = true; break; }
                    bool transferred = false;
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        MediaTimestamp time = packet.PresentationTime
                            ?? throw new NotSupportedException("Packet windows require known presentation timestamps.");
                        if (packet.StreamIndex != Stream.StreamIndex || (previous is MediaTimestamp last && time.CompareTo(last) < 0))
                        { throw new InvalidDataException("Packet windows require the selected stream and nondecreasing presentation timestamps."); }
                        previous = time;
                        if (time.CompareTo(selection.End) >= 0) { ended = true; break; }
                        if (time.CompareTo(selection.Start) >= 0)
                        {
                            transferred = true;
                            success = true;
                            return packet;
                        }
                    }
                    finally { if (!transferred) { packet.Dispose(); } }
                }
                success = true;
                return null;
            }
            finally { if (!success) { faulted = true; } Volatile.Write(ref busy, 0); }
        }

        public async ValueTask DisposeAsync()
        {
            Enter();
            try
            {
                if (!disposed) { disposed = true; await source.DisposeAsync().ConfigureAwait(false); }
            }
            finally { Volatile.Write(ref busy, 0); }
            GC.SuppressFinalize(this);
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) { throw new InvalidOperationException("Concurrent packet-window operations are not supported."); }
        }
    }
}
