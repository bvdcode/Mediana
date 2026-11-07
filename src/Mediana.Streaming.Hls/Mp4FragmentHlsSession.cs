// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Streaming.Hls
{
    /// <summary>Indexes VOD from MP4 declarations and writes independently requested fMP4 segments.
    /// Opening reads the index and required codec configuration; AVC random access is checked when its segment is requested.
    /// Borrows an immutable source, owns its reader, and requires serialized operations.</summary>
    public class Mp4FragmentHlsSession : IAsyncDisposable
    {
        private readonly Mp4Reader reader;
        private readonly MediaBufferAllocator allocator;
        private readonly Mp4FragmentHlsOptions options;
        private readonly int[] indices;
        private readonly H264RandomAccessInspector inspector;
        private bool disposed;
        private int busy;

        private Mp4FragmentHlsSession(Mp4Reader reader, MediaBufferAllocator allocator,
            Mp4FragmentHlsOptions options, int[] indices, Mp4HlsSegment[] segments)
        {
            this.reader = reader;
            this.allocator = allocator;
            this.options = options;
            this.indices = indices;
            inspector = new(reader.Streams[indices[0]]);
            Streams = Array.AsReadOnly(indices.Select(index => reader.Streams[index]).ToArray());
            Segments = Array.AsReadOnly(segments);
        }

        public IReadOnlyList<EncodedStreamInfo> Streams { get; }
        public IReadOnlyList<Mp4HlsSegment> Segments { get; }

        public static async ValueTask<Mp4FragmentHlsSession> OpenAsync(IRandomAccessByteSource source,
            MediaBufferAllocator allocator, int videoStreamIndex, int? audioStreamIndex = null,
            Mp4FragmentHlsOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(allocator);
            options ??= new();
            options.Validate();
            Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, options.Reading, cancellationToken).ConfigureAwait(false);
            bool success = false;
            try
            {
                int[] indices = audioStreamIndex is int audio ? [videoStreamIndex, audio] : [videoStreamIndex];
                if (indices.Distinct().Count() != indices.Length) { throw new ArgumentException("Video and audio streams must differ.", nameof(audioStreamIndex)); }
                foreach (int stream in indices)
                {
                    ArgumentOutOfRangeException.ThrowIfNegative(stream);
                    ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(stream, reader.Streams.Count);
                }
                if (audioStreamIndex is int selected && (reader.Streams[selected].Kind != MediaKind.Audio || reader.Streams[selected].CodecId != "aac"))
                {
                    throw new NotSupportedException("The selected fMP4 HLS audio stream must use AAC.");
                }
                Mp4AvcSegmentPlan plan = await Mp4AvcSegmentPlan.CreateFromIndexAsync(reader, videoStreamIndex,
                    options.Planning, cancellationToken).ConfigureAwait(false);
                Mp4HlsAudioRange[]? audioRanges = audioStreamIndex.HasValue ?
                    Mp4HlsAudioIndex.Create(reader, audioStreamIndex.Value, plan.Segments, cancellationToken) : null;
                Mp4HlsSegment[] segments = plan.Segments.Select((segment, number) =>
                    new Mp4HlsSegment(segment, audioRanges?[number], number + 1 == plan.Segments.Count)).ToArray();
                cancellationToken.ThrowIfCancellationRequested();
                Mp4FragmentHlsSession result = new(reader, allocator, options, indices, segments);
                success = true;
                return result;
            }
            finally { if (!success) { await reader.DisposeAsync().ConfigureAwait(false); } }
        }

        public string CreateVodPlaylist(string initializationUri, Func<int, string> segmentUri)
        {
            ArgumentException.ThrowIfNullOrEmpty(initializationUri);
            return HlsVodPlaylist.Create(Segments, static segment => segment.Duration, segmentUri,
                options.MaximumSegmentUriCharacters, initializationUri: initializationUri);
        }

        public async ValueTask WriteInitializationAsync(Stream destination, CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                await Mp4FragmentWriter.WriteInitializationAsync(reader, indices, destination, allocator, cancellationToken).ConfigureAwait(false);
            }
            finally { Volatile.Write(ref busy, 0); }
        }

        public async ValueTask WriteSegmentAsync(int number, Stream destination, CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                ArgumentOutOfRangeException.ThrowIfNegative(number);
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(number, Segments.Count);
                ArgumentNullException.ThrowIfNull(destination);
                if (!destination.CanWrite) { throw new ArgumentException("The segment destination must be writable.", nameof(destination)); }
                Mp4HlsSegment segment = Segments[number];
                using (MediaPacket opening = await reader.ReadPacketAtAsync(indices[0], segment.Video.FirstSample,
                    cancellationToken).ConfigureAwait(false))
                {
                    if (!inspector.IsIdr(opening, cancellationToken))
                    {
                        throw new InvalidDataException("The MP4 sync declaration does not identify an AVC IDR picture.");
                    }
                }
                List<Mp4SampleRange> ranges = [new(indices[0], segment.Video.FirstSample, segment.Video.SampleCount)];
                if (segment.Audio is Mp4HlsAudioRange audio && audio.SampleCount > 0)
                {
                    ranges.Add(new(audio.StreamIndex, audio.FirstSample, audio.SampleCount));
                }
                await Mp4FragmentWriter.WriteFragmentAsync(reader, ranges, checked((uint)number + 1), destination,
                    allocator, cancellationToken).ConfigureAwait(false);
            }
            finally { Volatile.Write(ref busy, 0); }
        }

        public async ValueTask DisposeAsync()
        {
            Enter();
            try
            {
                if (!disposed)
                {
                    disposed = true;
                    await reader.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally { Volatile.Write(ref busy, 0); }
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("Concurrent MP4 fragment HLS operations are not supported.");
            }
        }
    }
}
