using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Containers.Mp4
{
    /// <summary>Reads non-fragmented, self-contained MP4/MOV tracks in decode order.
    /// Packet timestamps use the original media timeline; edit lists are exposed, not applied.
    /// Borrows the source. Each packet belongs to its caller.</summary>
    public class Mp4Reader : IContainerReader
    {
        private readonly IRandomAccessByteSource source;
        private readonly MediaBufferAllocator allocator;
        private readonly MediaBuffer metadata;
        private readonly Mp4TrackCursor[] cursors;
        private bool disposed;
        private int busy;

        private Mp4Reader(IRandomAccessByteSource source, MediaBufferAllocator allocator,
            MediaBuffer metadata, Mp4Movie movie)
        {
            this.source = source;
            this.allocator = allocator;
            cursors = movie.Tables.Select(table => new Mp4TrackCursor(table)).ToArray();
            MovieTimeBase = movie.TimeBase;
            Duration = movie.Duration;
            Tracks = Array.AsReadOnly(movie.Tracks);
            Streams = Array.AsReadOnly(movie.Tracks.Select(track => track.Stream).ToArray());
            this.metadata = metadata.Retain();
        }

        public TimeBase MovieTimeBase { get; }
        public MediaDuration? Duration { get; }
        public IReadOnlyList<Mp4TrackInfo> Tracks { get; }
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
            cancellationToken.ThrowIfCancellationRequested();
            return new Mp4Reader(source, allocator, layout.Metadata, movie);
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
                using WritableMediaBuffer writable = allocator.Allocate(location.Size);
                await source.ReadExactlyAtAsync(location.Offset, writable.Memory, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                using MediaBuffer buffer = writable.Publish();
                TimeBase timeBase = Streams[selected].TimeBase;
                MediaPacket packet = new(selected, buffer, new MediaTimestamp(location.PresentationTime, timeBase),
                    new MediaTimestamp(location.DecodeTime, timeBase), new MediaDuration(location.Duration, timeBase),
                    location.IsSync);
                cursor.Advance(location);
                return packet;
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }

        private int SelectTrack()
        {
            int selected = -1;
            MediaTimestamp? earliest = null;
            for (int index = 0; index < cursors.Length; index++)
            {
                if (cursors[index].HasSample)
                {
                    MediaTimestamp timestamp = new(cursors[index].Current.DecodeTime, Streams[index].TimeBase);
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
