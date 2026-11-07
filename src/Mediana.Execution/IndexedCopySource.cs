// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Timing;

namespace Mediana.Execution
{
    /// <summary>Copies an explicit decode-order packet range with zero-copy track remapping.
    /// Borrows the indexed reader; range selection must already establish dependency and presentation boundaries.</summary>
    public class IndexedCopySource : IEncodedPacketSource
    {
        private readonly IIndexedContainerReader reader;
        private readonly int inputStreamIndex;
        private readonly long end;
        private readonly MediaReadBudget? budget;
        private long position;
        private bool disposed;
        private bool faulted;
        private bool ended;
        private int busy;

        public IndexedCopySource(IIndexedContainerReader reader, int inputStreamIndex, int outputStreamIndex,
            long firstPacket, long packetCount, MediaReadBudget? budget = null)
        {
            ArgumentNullException.ThrowIfNull(reader);
            ArgumentOutOfRangeException.ThrowIfNegative(firstPacket);
            ArgumentOutOfRangeException.ThrowIfNegative(packetCount);
            EncodedStreamInfo original = reader.Streams.SingleOrDefault(item => item.StreamIndex == inputStreamIndex)
                ?? throw new ArgumentOutOfRangeException(nameof(inputStreamIndex));
            long count = reader.GetPacketCount(inputStreamIndex);
            if (firstPacket > count || packetCount > count - firstPacket) { throw new ArgumentOutOfRangeException(nameof(packetCount)); }
            MediaDuration? codedDuration = null;
            if (firstPacket == 0 && packetCount == count) { codedDuration = original.CodedDuration; }
            Stream = new(outputStreamIndex, original.Kind, original.CodecId, original.Format, original.TimeBase,
                original.Configuration.Span, original.DisplayMatrix, codedDuration, original.ColorDescription, original.AspectRatioDescription);
            this.reader = reader;
            this.inputStreamIndex = inputStreamIndex;
            this.budget = budget;
            position = firstPacket;
            end = checked(firstPacket + packetCount);
        }

        public EncodedStreamInfo Stream { get; }

        public async ValueTask<MediaPacket?> ReadPacketAsync(CancellationToken cancellationToken = default)
        {
            Enter();
            bool success = false;
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (faulted) { throw new InvalidOperationException("The packet copy source has failed."); }
                cancellationToken.ThrowIfCancellationRequested();
                if (ended) { success = true; return null; }
                budget?.CountPacketRead();
                if (position == end) { ended = true; success = true; return null; }
                using MediaPacket packet = await reader.ReadPacketAtAsync(inputStreamIndex, position, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (packet.StreamIndex != inputStreamIndex) { throw new InvalidDataException("The indexed packet changed its stream identity."); }
                using Mediana.Core.Memory.MediaBuffer data = packet.RetainData();
                MediaPacket result = new(Stream.StreamIndex, data, packet.PresentationTime, packet.DecodeTime, packet.Duration, packet.IsKeyFrame);
                position++;
                success = true;
                return result;
            }
            finally { faulted |= !success; Volatile.Write(ref busy, 0); }
        }

        public ValueTask DisposeAsync()
        {
            Enter();
            try { disposed = true; return ValueTask.CompletedTask; }
            finally { Volatile.Write(ref busy, 0); GC.SuppressFinalize(this); }
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            { throw new InvalidOperationException("Packet copy operations must not overlap."); }
        }
    }
}
