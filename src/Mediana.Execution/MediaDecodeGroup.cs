// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Timing;

namespace Mediana.Execution
{
    /// <summary>One demuxer feeding selected decoders with no intermediate packet queue.
    /// Reads input only when all active decoders require it. Output order is per-stream, not globally sorted by PTS.
    /// Owns decoder tracks after successful construction; borrows the reader. Operations and disposal must not overlap.</summary>
    public class MediaDecodeGroup : IDisposable
    {
        private readonly IContainerReader reader;
        private readonly MediaDecodeTrack[] tracks;
        private readonly Dictionary<int, MediaDecodeTrack> byStream;
        private readonly IIndexedPresentationTimingReader? presentationTiming;
        private readonly Dictionary<int, long> packetPositions = [];
        private readonly bool[] ended;
        private readonly long? maxFrameReads;
        private long reads;
        private int nextTrack;
        private bool inputCompleted;
        private bool faulted;
        private bool disposed;
        private int busy;

        public MediaDecodeGroup(IContainerReader reader, IReadOnlyList<MediaDecodeTrack> tracks, long? maxFrameReads = null)
            : this(reader, tracks, maxFrameReads, reader as IIndexedPresentationTimingReader)
        {
        }

        /// <summary>The timing reader must describe the same packet sequence, starting at packet zero per stream.
        /// Both readers remain borrowed; the timing reader may be the wrapped source of the packet reader.</summary>
        public MediaDecodeGroup(IContainerReader reader, IReadOnlyList<MediaDecodeTrack> tracks, long? maxFrameReads,
            IIndexedPresentationTimingReader? presentationTiming)
        {
            ArgumentNullException.ThrowIfNull(reader);
            ArgumentNullException.ThrowIfNull(tracks);
            if (tracks.Count == 0)
            {
                throw new ArgumentException("Select at least one decoder track.", nameof(tracks));
            }
            if (maxFrameReads is long limit)
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
            }
            byStream = [];
            this.tracks = tracks.ToArray();
            foreach (MediaDecodeTrack track in this.tracks)
            {
                ArgumentNullException.ThrowIfNull(track);
                if (!reader.Streams.Contains(track.Stream) || !byStream.TryAdd(track.Stream.StreamIndex, track))
                {
                    throw new ArgumentException("Each decoder must select a unique stream declaration from the reader.", nameof(tracks));
                }
            }
            this.reader = reader;
            this.presentationTiming = presentationTiming;
            this.maxFrameReads = maxFrameReads;
            ended = new bool[tracks.Count];
        }

        public async ValueTask<DecodedMediaFrame?> ReadAsync(CancellationToken cancellationToken = default)
        {
            Enter();
            bool success = false;
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (faulted)
                {
                    throw new InvalidOperationException("The decode group has failed and must be disposed.");
                }
                if (maxFrameReads is long limit && reads++ == limit)
                {
                    throw new InvalidOperationException($"The configured session limit of {limit} frame reads has been reached.");
                }
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int active = 0;
                    for (int attempt = 0; attempt < tracks.Length; attempt++)
                    {
                        int index = nextTrack;
                        nextTrack = (nextTrack + 1) % tracks.Length;
                        if (ended[index])
                        {
                            continue;
                        }
                        CodecOutput<DecodedMediaFrame> output = tracks[index].Receive(cancellationToken);
                        switch (output.Status)
                        {
                            case CodecOutputStatus.Available:
                                success = true;
                                return output.Value;
                            case CodecOutputStatus.EndOfStream when inputCompleted:
                                ended[index] = true;
                                break;
                            case CodecOutputStatus.InputRequired when !inputCompleted:
                                active++;
                                break;
                            default:
                                throw new InvalidOperationException("The decoder returned an invalid state for the demux input lifetime.");
                        }
                    }
                    if (inputCompleted && active == 0)
                    {
                        success = true;
                        return null;
                    }
                    using MediaPacket? packet = await reader.ReadPacketAsync(cancellationToken).ConfigureAwait(false);
                    if (packet is null)
                    {
                        foreach (MediaDecodeTrack track in tracks)
                        {
                            track.CompleteInput();
                        }
                        inputCompleted = true;
                    }
                    else if (byStream.TryGetValue(packet.StreamIndex, out MediaDecodeTrack? selected))
                    {
                        using MediaPacket? timed = PreparePacket(packet, cancellationToken);
                        if (selected.Send(timed ?? packet, cancellationToken) != CodecInputStatus.Accepted)
                        {
                            throw new InvalidOperationException("The decoder refused input after requesting it.");
                        }
                    }
                }
            }
            finally
            {
                try
                {
                    if (!success)
                    {
                        faulted = true;
                        ReleaseTracks();
                    }
                }
                finally
                {
                    Volatile.Write(ref busy, 0);
                }
            }
        }

        public void Dispose()
        {
            Enter();
            try
            {
                if (!disposed)
                {
                    disposed = true;
                    ReleaseTracks();
                }
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
            GC.SuppressFinalize(this);
        }

        private MediaPacket? PreparePacket(MediaPacket packet, CancellationToken cancellationToken)
        {
            if (presentationTiming is null)
            {
                return null;
            }
            long position = packetPositions.GetValueOrDefault(packet.StreamIndex);
            packetPositions[packet.StreamIndex] = checked(position + 1);
            MediaDuration? duration = presentationTiming.GetPresentationDuration(packet.StreamIndex, position, cancellationToken);
            return packet.Duration == duration ? null : PacketPresentationTiming.Apply(packet.Retain(), duration);
        }

        private void ReleaseTracks()
        {
            foreach (MediaDecodeTrack track in tracks)
            {
                track.Dispose();
            }
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("Concurrent decode-group operations are not supported.");
            }
        }
    }
}
