// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Formats.Video;
using Mediana.IO;

namespace Mediana.Containers.Avi
{
    internal class AviVisualIndexBuilder : IDisposable
    {
        private readonly AviTrack track;
        private readonly MediaBufferAllocator allocator;
        private readonly AviVisualHeaderProbe probe;
        private readonly Mpeg4VisualClock clock = new();
        private AviPacketIndex? normalized;
        private int resolution;
        private int? lastReferenceIncrement;
        private long? origin, lastKey, pendingReference, previousDisplayed;
        private AviPacketIndex Index => normalized ?? track.Packets;

        internal AviVisualIndexBuilder(IRandomAccessByteSource source, MediaBufferAllocator allocator, AviTrack track)
        {
            this.track = track; this.allocator = allocator;
            probe = new(source, allocator, track.Stream.Configuration);
        }

        internal async ValueTask BuildAsync(CancellationToken token)
        {
            AviPacketIndex physical = track.Packets;
            for (long index = 0; index < physical.Count; index++)
            {
                AviPacketEntry entry = physical[index];
                Mpeg4VopTiming header = await probe.ReadAsync(entry, clock, token).ConfigureAwait(false);
                EnsureClock();
                if (probe.IsPackedBitstream)
                {
                    EnsureNormalized(index, token);
                    if (!header.IsCoded)
                    {
                        if (header.Type == Mpeg4VopType.Bidirectional || header.ModuloTimeBase != 0 || header.TimeIncrement != lastReferenceIncrement)
                        { throw new NotSupportedException("An uncoded AVI VOP advances presentation and is not a packed transport placeholder."); }
                        await probe.ValidateUncodedPaddingAsync(entry, probe.PictureOffset, token).ConfigureAwait(false);
                        continue;
                    }
                    long first = normalized!.Count;
                    if (header.Type == Mpeg4VopType.Bidirectional) { normalized.Add(entry); }
                    else { await probe.AppendPackedRangesAsync(entry, probe.PictureOffset, normalized, token).ConfigureAwait(false); }
                    normalized.Replace(first, Map(entry with { Length = normalized[first].Length }, header));
                    RecordPresentation(first, header.Type);
                    for (long part = first + 1; part < normalized.Count; part++)
                    {
                        AviPacketEntry range = normalized[part];
                        Mpeg4VopTiming secondary = await probe.ReadAsync(range, clock, token).ConfigureAwait(false);
                        EnsureClock();
                        if (!secondary.IsCoded || secondary.Type != Mpeg4VopType.Bidirectional)
                        { throw new NotSupportedException("A packed AVI reference VOP can only be followed by coded B VOPs."); }
                        normalized.Replace(part, Map(range, secondary)); RecordPresentation(part, secondary.Type);
                    }
                }
                else
                {
                    if (!header.IsCoded) { throw new NotSupportedException("An uncoded MPEG-4 Visual VOP does not represent a complete AVI presentation unit."); }
                    physical.Replace(index, Map(entry, header)); RecordPresentation(index, header.Type);
                }
            }
            if (pendingReference is long pending) { ReleasePresentation(pending); }
            if (previousDisplayed is long last)
            {
                AviPacketEntry entry = Index[last];
                long duration = new MediaTimestamp(entry.Duration, track.Stream.TimeBase).Rescale(track.PresentationClock, TimestampRounding.TowardZero).Value;
                Index.Replace(last, entry with { PresentationDuration = duration });
            }
            if (origin is not null) { track.SetConfiguration(probe.OpeningConfiguration()); }
            if (normalized is not null) { track.SetPackets(normalized); normalized = null; }
        }

        private void EnsureClock()
        {
            Mpeg4VisualConfiguration config = probe.Configuration!;
            if (resolution == 0)
            {
                resolution = config.TimeIncrementResolution;
                track.PresentationClock = TimeBase.Common(track.Stream.TimeBase, new(1, resolution));
            }
            else if (resolution != config.TimeIncrementResolution)
            { throw new NotSupportedException("Changing MPEG-4 Visual clock resolutions within one AVI stream are not supported."); }
        }

        private void EnsureNormalized(long index, CancellationToken token)
        {
            if (normalized is not null) { return; }
            normalized = new(allocator);
            for (long previous = 0; previous < index; previous++)
            { token.ThrowIfCancellationRequested(); normalized.Add(track.Packets[previous]); }
        }

        private AviPacketEntry Map(AviPacketEntry entry, Mpeg4VopTiming header)
        {
            long time = clock.Read(header, probe.Configuration!); origin ??= time;
            MediaTimestamp relative = new(time - origin.Value, new(1, resolution));
            long presentation = relative.Rescale(track.PresentationClock, TimestampRounding.TowardZero).Value;
            long start = new MediaTimestamp(track.Start, track.Stream.TimeBase).Rescale(track.PresentationClock, TimestampRounding.TowardZero).Value;
            bool keyFrame = header.Type == Mpeg4VopType.Intra;
            if (keyFrame)
            {
                if (lastKey is long previous && presentation <= previous)
                { throw new InvalidDataException("AVI MPEG-4 Visual random-access picture clocks must increase."); }
                lastKey = presentation;
            }
            if (header.Type != Mpeg4VopType.Bidirectional) { lastReferenceIncrement = header.TimeIncrement; }
            return entry with { PresentationTime = checked(presentation + start), KeyFrame = keyFrame };
        }

        private void RecordPresentation(long index, Mpeg4VopType type)
        {
            if (type == Mpeg4VopType.Bidirectional) { ReleasePresentation(index); }
            else
            {
                if (pendingReference is long pending) { ReleasePresentation(pending); }
                pendingReference = index;
            }
        }

        private void ReleasePresentation(long index)
        {
            AviPacketEntry current = Index[index];
            if (previousDisplayed is long previous)
            {
                AviPacketEntry prior = Index[previous];
                long duration = checked(current.PresentationTime!.Value - prior.PresentationTime!.Value);
                if (duration <= 0) { throw new InvalidDataException("AVI MPEG-4 Visual presentation clocks must increase after reordering."); }
                Index.Replace(previous, prior with { PresentationDuration = duration });
            }
            previousDisplayed = index;
        }

        public void Dispose() { probe.Dispose(); normalized?.Dispose(); normalized = null; }
    }
}
