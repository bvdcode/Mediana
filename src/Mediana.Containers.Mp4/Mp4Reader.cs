// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;

namespace Mediana.Containers.Mp4
{
    /// <summary>Reads non-fragmented, self-contained MP4/MOV tracks in decode order.
    /// Packet timestamps use the selected timeline; every encoded sample is retained.
    /// Borrows the source. Each packet belongs to its caller.</summary>
    public partial class Mp4Reader : IIndexedContainerReader
    {
        private readonly IRandomAccessByteSource source;
        private readonly MediaBufferAllocator allocator;
        private readonly MediaBuffer metadata;
        private readonly Mp4MetadataValue[] metadataValues;
        private readonly Mp4TrackCursor[] cursors;
        private readonly Mp4LinearTimeline[] timelines;
        private readonly Mp4SampleLookup[] lookups;
        private bool disposed;
        private int busy;

        private Mp4Reader(IRandomAccessByteSource source, MediaBufferAllocator allocator,
            MediaBuffer metadata, Mp4Movie movie, Mp4TimestampMode timestampMode)
        {
            this.source = source;
            this.allocator = allocator;
            lookups = movie.Tables.Select(table => new Mp4SampleLookup(table)).ToArray();
            cursors = movie.Tables.Select(table => new Mp4TrackCursor(table)).ToArray();
            MovieTimeBase = movie.TimeBase;
            Duration = movie.Duration;
            DisplayMatrix = movie.DisplayMatrix;
            Tracks = Array.AsReadOnly(movie.Tracks);
            metadataValues = movie.Metadata;
            Metadata = Array.AsReadOnly(movie.Metadata.Select(value => value.Item).ToArray());
            CoverArt = Array.AsReadOnly(Mp4MetadataParser.GetCoverArt(movie.Metadata));
            TimestampMode = timestampMode;
            timelines = new Mp4LinearTimeline[movie.Tracks.Length];
            EncodedStreamInfo[] streams = new EncodedStreamInfo[movie.Tracks.Length];
            for (int index = 0; index < streams.Length; index++)
            {
                Mp4TrackInfo track = movie.Tracks[index];
                EncodedStreamInfo original = track.Stream;
                Mp4LinearTimeline timeline = timestampMode switch
                {
                    Mp4TimestampMode.Media => Mp4LinearTimeline.Identity(original.TimeBase),
                    Mp4TimestampMode.LinearMovie => Mp4LinearTimeline.Create(original.TimeBase, track.Edits),
                    _ => throw new ArgumentOutOfRangeException(nameof(timestampMode))
                };
                timelines[index] = timeline;
                streams[index] = timeline.TimeBase == original.TimeBase ? original :
                    new EncodedStreamInfo(original.StreamIndex, original.Kind, original.CodecId,
                        timeline.TimeBase, original.Configuration.Span, original.DisplayMatrix, original.CodedDuration);
            }
            Streams = Array.AsReadOnly(streams);
            this.metadata = metadata.Retain();
        }

        public TimeBase MovieTimeBase { get; }
        public MediaDuration? Duration { get; }
        /// <summary>The movie header display matrix, applied after each track header matrix.</summary>
        public VideoDisplayMatrix DisplayMatrix { get; }
        public IReadOnlyList<Mp4TrackInfo> Tracks { get; }
        public IReadOnlyList<Mp4MetadataItem> Metadata { get; }
        public IReadOnlyList<Mp4CoverArtInfo> CoverArt { get; }
        public Mp4TimestampMode TimestampMode { get; }
        /// <summary>Packet stream configurations in the selected time base. Tracks preserve original declarations.</summary>
        public IReadOnlyList<EncodedStreamInfo> Streams { get; }

        public static async ValueTask<Mp4Reader> OpenAsync(IRandomAccessByteSource source,
            MediaBufferAllocator allocator, Mp4ReadOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(allocator);
            options ??= new Mp4ReadOptions();
            options.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            Mp4ParseBudget budget = new(options, cancellationToken);
            using Mp4FileLayout layout = await Mp4FileLayout.ReadAsync(source, allocator,
                options, budget, cancellationToken).ConfigureAwait(false);
            Mp4Movie movie = Mp4MovieParser.Parse(layout.Metadata.Memory, layout.DataRanges,
                options, budget, cancellationToken);
            await Mp4AudioConfiguration.ReadAsync(movie, source, allocator, options, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new Mp4Reader(source, allocator, layout.Metadata, movie, options.TimestampMode);
        }

        public async ValueTask<MediaPacket?> ReadPacketAsync(CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                cancellationToken.ThrowIfCancellationRequested();
                int selected = SelectTrack();
                if (selected < 0)
                {
                    return null;
                }
                Mp4TrackCursor cursor = cursors[selected];
                Mp4SampleLocation location = cursor.Current;
                MediaPacket packet = await ReadSampleAsync(selected, location, cancellationToken).ConfigureAwait(false);
                cursor.Advance(location);
                return packet;
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }

        /// <summary>Resolves sample metadata without reading media bytes or moving the sequential cursors.
        /// Successive lookups advance a separate metadata cursor. Other lookups scan table runs and
        /// sizes within the containing chunk; no per-sample index is allocated.</summary>
        public Mp4SampleInfo GetSampleInfo(int streamIndex, int sampleIndex, CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                Mp4SampleLocation location = Locate(streamIndex, sampleIndex, cancellationToken);
                Mp4LinearTimeline timeline = timelines[streamIndex];
                return new(streamIndex, sampleIndex, location.Offset, location.Size,
                    timeline.Map(location.PresentationTime), timeline.Map(location.DecodeTime),
                    timeline.MapDuration(location.Duration), location.IsSync);
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }

        /// <summary>Reads one encoded sample by its zero-based decode-order index.
        /// Does not move sequential cursors or reset decoder state. The returned packet belongs to its caller.</summary>
        public async ValueTask<MediaPacket> ReadPacketAtAsync(int streamIndex, int sampleIndex,
            CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                Mp4SampleLocation location = Locate(streamIndex, sampleIndex, cancellationToken);
                return await ReadSampleAsync(streamIndex, location, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }

        /// <summary>Returns an owned view of the original data atom value without moving any packet cursor.
        /// The view survives reader disposal and retains the complete movie metadata allocation.</summary>
        public MediaBuffer ReadMetadataValue(int itemIndex)
        {
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                ArgumentOutOfRangeException.ThrowIfNegative(itemIndex);
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(itemIndex, metadataValues.Length);
                Mp4MetadataValue value = metadataValues[itemIndex];
                return metadata.Slice(value.Offset, value.Item.ByteLength);
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }

        public MediaBuffer ReadCoverArt(int coverIndex)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(coverIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(coverIndex, CoverArt.Count);
            return ReadMetadataValue(CoverArt[coverIndex].MetadataItemIndex);
        }

        private Mp4SampleLocation Locate(int streamIndex, int sampleIndex, CancellationToken cancellationToken)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(streamIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(streamIndex, lookups.Length);
            return lookups[streamIndex].Find(sampleIndex, cancellationToken);
        }

        private async ValueTask<MediaPacket> ReadSampleAsync(int streamIndex, Mp4SampleLocation location,
            CancellationToken cancellationToken)
        {
            Mp4LinearTimeline timeline = timelines[streamIndex];
            MediaTimestamp presentationTime = timeline.Map(location.PresentationTime);
            MediaTimestamp decodeTime = timeline.Map(location.DecodeTime);
            MediaDuration duration = timeline.MapDuration(location.Duration);
            using WritableMediaBuffer writable = allocator.Allocate(location.Size);
            await source.ReadExactlyAtAsync(location.Offset, writable.Memory, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            using MediaBuffer buffer = writable.Publish();
            return new(streamIndex, buffer, presentationTime, decodeTime, duration, location.IsSync);
        }

        private int SelectTrack()
        {
            int selected = -1;
            MediaTimestamp? earliest = null;
            for (int index = 0; index < cursors.Length; index++)
            {
                if (cursors[index].HasSample)
                {
                    MediaTimestamp timestamp = timelines[index].Map(cursors[index].Current.DecodeTime);
                    if (earliest is null || timestamp.CompareTo(earliest.Value) < 0)
                    {
                        selected = index;
                        earliest = timestamp;
                    }
                }
            }
            return selected;
        }

        public ValueTask DisposeAsync()
        {
            Enter();
            try
            {
                if (!disposed)
                {
                    disposed = true;
                    metadata.Dispose();
                }
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
            GC.SuppressFinalize(this);
            return ValueTask.CompletedTask;
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("Concurrent MP4 reader operations are not supported.");
            }
        }
    }
}
