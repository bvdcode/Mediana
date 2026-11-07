// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Timing;

namespace Mediana.Execution
{
    /// <summary>Merges known decode times with at most one owned pending packet per source.
    /// Borrows sources and writer. Source order breaks equal-time ties. Each stream must have strictly increasing DTS.
    /// Success finalizes the writer; failure releases pending packets without finalizing it.</summary>
    public static class MediaPacketMerge
    {
        public static Task<long> WriteAsync(IReadOnlyList<IEncodedPacketSource> sources, IContainerWriter writer,
            CancellationToken cancellationToken = default) => WriteAsync(sources, writer, null, cancellationToken);

        /// <summary>Invokes the observer after each successful packet write. The packet is borrowed only
        /// for the callback; retain it explicitly to extend its lifetime. Cancellation aborts without completing the writer.</summary>
        public static async Task<long> WriteAsync(IReadOnlyList<IEncodedPacketSource> sources, IContainerWriter writer,
            Action<MediaPacket>? packetWritten, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(sources);
            ArgumentNullException.ThrowIfNull(writer);
            IEncodedPacketSource[] inputs = sources.ToArray();
            if (inputs.Length == 0 || inputs.Any(source => source is null))
            {
                throw new ArgumentException("Select nonempty sources with unique stream indices.", nameof(sources));
            }
            int[] streamIndices = inputs.Select(source => source.Stream.StreamIndex).ToArray();
            if (streamIndices.Distinct().Count() != inputs.Length)
            {
                throw new ArgumentException("Select nonempty sources with unique stream indices.", nameof(sources));
            }
            MediaPacket?[] pending = new MediaPacket?[inputs.Length];
            MediaTimestamp?[] previous = new MediaTimestamp?[inputs.Length];
            bool[] ended = new bool[inputs.Length];
            long written = 0;
            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int selected = -1;
                    for (int index = 0; index < inputs.Length; index++)
                    {
                        if (pending[index] is null && !ended[index])
                        {
                            pending[index] = await inputs[index].ReadPacketAsync(cancellationToken).ConfigureAwait(false);
                            cancellationToken.ThrowIfCancellationRequested();
                            ended[index] = pending[index] is null;
                            if (pending[index] is MediaPacket packet)
                            {
                                MediaTimestamp timestamp = packet.DecodeTime
                                    ?? throw new NotSupportedException("Packet merging requires known decode times.");
                                if (packet.StreamIndex != streamIndices[index] ||
                                    (previous[index] is MediaTimestamp last && timestamp.CompareTo(last) <= 0))
                                {
                                    throw new InvalidDataException("An encoding source changed its stream or returned unordered decode times.");
                                }
                                previous[index] = timestamp;
                            }
                        }
                        if (pending[index] is MediaPacket next && (selected < 0 ||
                            next.DecodeTime!.Value.CompareTo(pending[selected]!.DecodeTime!.Value) < 0))
                        {
                            selected = index;
                        }
                    }
                    if (selected < 0)
                    {
                        await writer.CompleteAsync(cancellationToken).ConfigureAwait(false);
                        return written;
                    }
                    using MediaPacket output = pending[selected]!;
                    pending[selected] = null;
                    await writer.WritePacketAsync(output, cancellationToken).ConfigureAwait(false);
                    written = checked(written + 1);
                    packetWritten?.Invoke(output);
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
            finally
            {
                foreach (MediaPacket? packet in pending)
                {
                    packet?.Dispose();
                }
            }
        }
    }
}
