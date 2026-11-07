// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Timing;

namespace Mediana.Execution
{
    /// <summary>Selects exact packet-aligned presentation intervals with contiguous decode-order payload.
    /// Video starts and non-EOF ends require codec-validated independent access packets.
    /// Misaligned requests are rejected rather than enlarged or decoded implicitly.</summary>
    public static class IndexedCopyPlanner
    {
        public static async ValueTask<IndexedPacketRange> CreateAsync(IIndexedPacketTimingReader reader, int streamIndex,
            MediaTimeRange selection, IRandomAccessInspector? inspector = null, MediaReadBudget? budget = null,
            CancellationToken cancellationToken = default)
            => await CreateAsync(reader, streamIndex, selection, PacketCopyAlignment.Exact, inspector, budget, cancellationToken).ConfigureAwait(false);

        /// <summary>PresentationStarts assigns every packet to the half-open interval containing its presentation start.
        /// Audio packets may cross the selected end. Video still requires independent decode boundaries.</summary>
        public static async ValueTask<IndexedPacketRange> CreateAsync(IIndexedPacketTimingReader reader, int streamIndex,
            MediaTimeRange selection, PacketCopyAlignment alignment, IRandomAccessInspector? inspector = null,
            MediaReadBudget? budget = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(reader);
            if (!Enum.IsDefined(alignment)) { throw new ArgumentOutOfRangeException(nameof(alignment)); }
            selection.Start.TimeBase.Validate();
            selection.End.TimeBase.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            EncodedStreamInfo stream = reader.Streams.SingleOrDefault(item => item.StreamIndex == streamIndex)
                ?? throw new ArgumentOutOfRangeException(nameof(streamIndex));
            if (stream.Format.Boundary != EncodedPacketBoundary.AccessUnit || stream.Kind is not (MediaKind.Audio or MediaKind.Video))
            { throw new NotSupportedException("Interval copy requires complete audio or video access units."); }
            long count = reader.GetPacketCount(streamIndex);
            long first = -1;
            long last = -1;
            MediaTimestamp? earliest = null;
            MediaTimestamp? latest = null;
            long finalPresentationPacket = -1;
            bool closed = false;
            for (long index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ContainerPacketTiming timing = reader.GetPacketTiming(streamIndex, index, cancellationToken);
                MediaTimestamp pts = timing.PresentationTime ?? throw new NotSupportedException("Interval copy requires known presentation times.");
                bool selected = pts.CompareTo(selection.Start) >= 0 && pts.CompareTo(selection.End) < 0;
                if (!selected) { closed |= first >= 0; continue; }
                if (closed) { throw new NotSupportedException("The selected presentation interval is not contiguous in decode order."); }
                if (first < 0) { first = index; }
                last = index;
                if (earliest is null || pts.CompareTo(earliest.Value) < 0) { earliest = pts; }
                if (latest is null || pts.CompareTo(latest.Value) > 0) { latest = pts; finalPresentationPacket = index; }
            }
            switch (alignment)
            {
                case PacketCopyAlignment.Exact:
                    if (first < 0 || earliest != selection.Start)
                    { throw new NotSupportedException("Compressed copy requires a nonempty interval starting on a packet boundary."); }
                    ContainerPacketTiming final = reader.GetPacketTiming(streamIndex, finalPresentationPacket, cancellationToken);
                    MediaDuration duration = (reader is IIndexedPresentationTimingReader presentation
                        ? presentation.GetPresentationDuration(streamIndex, finalPresentationPacket, cancellationToken) : final.Duration)
                        ?? throw new NotSupportedException("Compressed copy requires a known final presentation duration.");
                    TimeBase clock = TimeBase.Common(latest!.Value.ReduceClock().TimeBase,
                        new MediaTimestamp(duration.Value, duration.TimeBase).ReduceClock().TimeBase);
                    MediaTimestamp end = new(checked(latest.Value.Rescale(clock, TimestampRounding.TowardZero).Value +
                        new MediaTimestamp(duration.Value, duration.TimeBase).Rescale(clock, TimestampRounding.TowardZero).Value), clock);
                    if (end != selection.End) { throw new NotSupportedException("Compressed copy requires an exact final packet boundary."); }
                    break;
                case PacketCopyAlignment.PresentationStarts:
                    if (first < 0) { return new(count, 0); }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(alignment));
            }
            if (stream.Kind == MediaKind.Video)
            {
                ArgumentNullException.ThrowIfNull(inspector);
                await VerifyRestartAsync(first, earliest!.Value).ConfigureAwait(false);
                if (last + 1 < count)
                {
                    MediaTimestamp boundary = reader.GetPacketTiming(streamIndex, last + 1, cancellationToken).PresentationTime
                        ?? throw new NotSupportedException("Video copy requires known next access-point timing.");
                    await VerifyRestartAsync(last + 1, boundary).ConfigureAwait(false);
                }
            }
            return new(first, checked(last - first + 1));

            async ValueTask VerifyRestartAsync(long index, MediaTimestamp expected)
            {
                budget?.CountPacketRead();
                using MediaPacket packet = await reader.ReadPacketAtAsync(streamIndex, index, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (packet.StreamIndex != streamIndex || packet.PresentationTime != expected || !inspector!.IsRandomAccess(packet, cancellationToken))
                { throw new NotSupportedException("Video copy boundaries require codec-validated independent access packets."); }
            }
        }
    }
}
