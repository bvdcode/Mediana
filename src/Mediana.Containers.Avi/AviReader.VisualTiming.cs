// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;
using Mediana.Formats.Video;

namespace Mediana.Containers.Avi
{
    public partial class AviReader
    {
        private async ValueTask ReadVisualClocksAsync(AviTrack track, CancellationToken token)
        {
            using AviVisualHeaderProbe probe = new(source, allocator, track.Stream.Configuration);
            Mpeg4VisualClock clock = new(); int resolution = 0; long? origin = null; long? lastKey = null;
            for (long index = 0; index < track.Packets.Count; index++)
            {
                AviPacketEntry entry = track.Packets[index]; Mpeg4VopTiming header = await probe.ReadAsync(entry, clock, token).ConfigureAwait(false);
                Mpeg4VisualConfiguration config = probe.Configuration!;
                if (resolution == 0)
                {
                    resolution = config.TimeIncrementResolution; track.PresentationClock = TimeBase.Common(track.Stream.TimeBase, new(1, resolution));
                }
                else if (resolution != config.TimeIncrementResolution)
                { throw new NotSupportedException("Changing MPEG-4 Visual clock resolutions within one AVI stream are not supported."); }
                if (!header.IsCoded) { throw new NotSupportedException("An uncoded MPEG-4 Visual VOP does not represent a complete AVI presentation unit."); }
                long time = clock.Read(header, config); origin ??= time;
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
                track.Packets.Replace(index, entry with { PresentationTime = checked(presentation + start), KeyFrame = keyFrame });
            }
            if (origin is not null) { track.SetConfiguration(probe.OpeningConfiguration()); }
        }
    }
}
