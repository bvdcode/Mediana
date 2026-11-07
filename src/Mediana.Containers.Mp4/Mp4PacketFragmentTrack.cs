// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Timing;

namespace Mediana.Containers.Mp4
{
    internal class Mp4PacketFragmentTrack : Mp4FragmentTrack
    {
        private readonly Mp4TrackDefinition track;
        private readonly IReadOnlyList<MediaPacket> packets;

        public Mp4PacketFragmentTrack(Mp4TrackDefinition track, IReadOnlyList<MediaPacket> packets, CancellationToken token)
        {
            this.track = track;
            this.packets = packets;
            if (packets.Count == 0) { throw new ArgumentException("A video fragment requires at least one packet.", nameof(packets)); }
            long? previousEnd = null;
            for (int index = 0; index < packets.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                MediaPacket packet = packets[index] ?? throw new ArgumentException("Fragment packets cannot be null.", nameof(packets));
                if (packet.StreamIndex != track.Stream.StreamIndex || packet.Data.IsEmpty ||
                    (track.Stream.Kind == MediaKind.Video && packet.IsKeyFrame is null))
                {
                    throw new InvalidDataException("A fragmented packet requires the declared stream, data and a sync indication.");
                }
                long decode = Decode(packet);
                if (decode < 0 || (previousEnd is long end && decode != end))
                {
                    throw new InvalidDataException("Fragment decode times must be nonnegative and contiguous.");
                }
                Mp4FragmentSample sample = GetSample(index, token);
                previousEnd = checked(decode + sample.Duration);
            }
        }

        public override uint TrackId => track.TrackId;
        public override ulong DecodeTime => checked((ulong)Decode(packets[0]));
        public override int SampleCount => packets.Count;

        public override Mp4FragmentSample GetSample(int index, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            MediaPacket packet = packets[index];
            MediaDuration duration = packet.Duration ?? throw new InvalidDataException("A fragment packet requires a duration.");
            long ticks = Exact(new(duration.Value, duration.TimeBase));
            if (ticks <= 0 || ticks > uint.MaxValue) { throw new InvalidDataException("A fragment sample duration exceeds its unsigned 32-bit range."); }
            long presentation = checked(Exact(packet.PresentationTime ?? throw new InvalidDataException("A fragment packet requires a presentation timestamp.")) + track.TimestampOffset);
            bool sync = track.Stream.Kind switch
            {
                MediaKind.Audio => true,
                MediaKind.Video => packet.IsKeyFrame!.Value,
                MediaKind.Data => throw new NotSupportedException("Data fragments require their own sample declaration."),
                _ => throw new ArgumentOutOfRangeException(nameof(track))
            };
            return new((uint)ticks, packet.Data.Length, sync, checked(presentation - Decode(packet)));
        }

        private long Decode(MediaPacket packet) => checked(Exact(packet.DecodeTime ?? throw new InvalidDataException("A fragment packet requires a decode timestamp.")) + track.TimestampOffset);

        private long Exact(MediaTimestamp timestamp)
        {
            MediaTimestamp normalized = timestamp.Rescale(track.TimeBase, TimestampRounding.TowardZero);
            if (timestamp != normalized) { throw new InvalidDataException("The packet timing is not exact on its MP4 track clock."); }
            return normalized.Value;
        }
    }
}
