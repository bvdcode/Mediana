// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Formats;
using Mediana.Formats.Audio;
using Mediana.IO;

namespace Mediana.Containers.Ac3
{
    /// <summary>Indexes bounded AC-3/E-AC-3 frame headers and opening channel maps without reading compressed audio payloads.
    /// Borrows a complete immutable random-access source. Returned packets own independent storage.</summary>
    public partial class Ac3Reader : IIndexedPrerollReader, IIndexedPacketTimingReader, IIndexedPresentationTimingReader
    {
        private readonly IRandomAccessByteSource source;
        private readonly MediaBufferAllocator allocator;
        private readonly List<Ac3FrameLocation> locations;
        private int cursor, busy;
        private bool disposed;

        private Ac3Reader(IRandomAccessByteSource source, MediaBufferAllocator allocator,
            Ac3FrameHeader first, Eac3FrameMetadata[]? program, List<Ac3FrameLocation> locations, long samples, long payloadBytes)
        {
            this.source = source; this.allocator = allocator; this.locations = locations;
            FirstFrame = first;
            ChannelOrder = Array.AsReadOnly(program is null ? first.GetChannelOrder() : Eac3ChannelMap.GetProgramChannelOrder(program));
            openingMetadata = new(first.SampleRate, ChannelOrder.Count, channelOrder: ChannelOrder)
            { CodecProfile = new(first.IsEnhanced ? "E-AC-3" : "AC-3") };
            TimeBase clock = new(1, first.SampleRate);
            MediaDuration duration = new(samples, clock);
            string codec = first.IsEnhanced ? Ac3FrameHeader.EnhancedCodecId : Ac3FrameHeader.CodecId;
            Streams = Array.AsReadOnly(new[] { new EncodedStreamInfo(0, MediaKind.Audio, codec,
                first.IsEnhanced ? StreamFormats.Eac3 : StreamFormats.Ac3, clock, codedDuration: duration) });
            Info = new(codec, streamInfo: [new(0, packetIndex: new(locations.Count, payloadBytes, duration))]);
        }

        public Ac3FrameHeader FirstFrame { get; }
        /// <summary>Rendered programme channels, including dependent replacements and extensions, in output order.</summary>
        public IReadOnlyList<string> ChannelOrder { get; }
        public ContainerInfo Info { get; }
        public IReadOnlyList<EncodedStreamInfo> Streams { get; }

        public static async ValueTask<Ac3Reader> OpenAsync(IRandomAccessByteSource source,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(allocator);
            long length = source.Length ?? throw new NotSupportedException("AC-3 indexing requires a known source length.");
            if (length < Ac3FrameHeader.HeaderBytes) { throw new InvalidDataException("The AC-3 source is empty or truncated."); }
            cancellationToken.ThrowIfCancellationRequested();
            using WritableMediaBuffer scratch = allocator.Allocate(Eac3FrameMetadata.MaximumPrefixBytes);
            List<Ac3FrameLocation> locations = [];
            Ac3FrameHeader? first = null;
            List<Eac3FrameMetadata> program = [];
            Ac3FrameHeader? current = null;
            int dependent = 0, expectedDependents = -1;
            long position = 0, samples = 0;
            while (position < length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (length - position < Ac3FrameHeader.HeaderBytes)
                { throw new InvalidDataException("The final AC-3 syncframe header is truncated."); }
                await source.ReadExactlyAtAsync(position, scratch.Memory[..Ac3FrameHeader.HeaderBytes], cancellationToken).ConfigureAwait(false);
                Ac3FrameHeader header = Ac3FrameHeader.Read(scratch.Memory.Span);
                first ??= header;
                if (header.FrameBytes > length - position)
                { throw new InvalidDataException("The AC-3 syncframe extends beyond the source."); }
                if (header.IsEnhanced && header.StreamType == 1)
                {
                    if (current is { IsEnhanced: false })
                    { throw new NotSupportedException("A combined AC-3 main and E-AC-3 dependent presentation is not supported."); }
                    if (current is null || current.StreamType != 0 || header.SubstreamId != dependent ||
                        header.SampleRate != current.SampleRate || header.SamplesPerFrame != current.SamplesPerFrame)
                    { throw new InvalidDataException("The dependent E-AC-3 syncframe is not ordered and synchronized with its independent frame."); }
                    dependent++;
                    Ac3FrameLocation previous = locations[^1];
                    locations[^1] = previous with { ByteLength = checked(previous.ByteLength + header.FrameBytes) };
                }
                else
                {
                    RequireConfiguration(first, header);
                    if (current is not null)
                    {
                        if (expectedDependents < 0) { expectedDependents = dependent; }
                        else if (dependent != expectedDependents)
                        { throw new NotSupportedException("E-AC-3 dependent substream count changes require a new track."); }
                    }
                    current = header; dependent = 0;
                    locations.Add(new(position, samples, header.FrameBytes, header.SamplesPerFrame));
                    samples = checked(samples + header.SamplesPerFrame);
                }
                if (first.IsEnhanced && locations.Count == 1)
                {
                    int bytes = Math.Min(header.FrameBytes, Eac3FrameMetadata.MaximumPrefixBytes);
                    await source.ReadExactlyAtAsync(position + Ac3FrameHeader.HeaderBytes,
                        scratch.Memory.Slice(Ac3FrameHeader.HeaderBytes, bytes - Ac3FrameHeader.HeaderBytes), cancellationToken).ConfigureAwait(false);
                    program.Add(Eac3FrameMetadata.Read(scratch.Memory.Span[..bytes]));
                }
                position = checked(position + header.FrameBytes);
            }
            if (expectedDependents >= 0 && dependent != expectedDependents)
            { throw new InvalidDataException("The final E-AC-3 frame set is missing dependent substreams."); }
            cancellationToken.ThrowIfCancellationRequested();
            return new(source, allocator, first!, first!.IsEnhanced ? program.ToArray() : null, locations, samples, length);
        }

        private static void RequireConfiguration(Ac3FrameHeader first, Ac3FrameHeader header)
        {
            if (header.IsEnhanced != first.IsEnhanced || header.SampleRate != first.SampleRate ||
                header.ChannelMode != first.ChannelMode || header.HasLfe != first.HasLfe ||
                header.SubstreamId != first.SubstreamId)
            { throw new NotSupportedException("AC-3 codec, sampling format or independent substream changes require a new track."); }
            if (header.IsEnhanced && (header.StreamType == 1 || header.SubstreamId != 0))
            { throw new NotSupportedException("Standalone E-AC-3 requires the selected independent substream zero."); }
        }

        public async ValueTask<MediaPacket?> ReadPacketAsync(CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                RequireStream(0); cancellationToken.ThrowIfCancellationRequested();
                if (cursor == locations.Count) { return null; }
                MediaPacket packet = await ReadCoreAsync(cursor, cancellationToken).ConfigureAwait(false);
                cursor++; return packet;
            }
            finally { Exit(); }
        }

        public async ValueTask<MediaPacket> ReadPacketAtAsync(int streamIndex, long packetIndex,
            CancellationToken cancellationToken = default)
        {
            Enter();
            try { return await ReadCoreAsync(RequirePacket(streamIndex, packetIndex), cancellationToken).ConfigureAwait(false); }
            finally { Exit(); }
        }

        private async ValueTask<MediaPacket> ReadCoreAsync(int index, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Ac3FrameLocation location = locations[index];
            using WritableMediaBuffer writable = allocator.Allocate(location.ByteLength);
            await source.ReadExactlyAtAsync(location.Offset, writable.Memory, token).ConfigureAwait(false);
            Ac3FrameHeader header = Ac3FrameHeader.Read(writable.Span);
            RequireConfiguration(FirstFrame, header);
            if (header.SamplesPerFrame != location.Samples)
            { throw new InvalidDataException("The AC-3 syncframe no longer matches its index."); }
            int offset = header.FrameBytes, dependent = 0;
            while (offset < location.ByteLength)
            {
                Ac3FrameHeader next = Ac3FrameHeader.Read(writable.Span[offset..]);
                if (!header.IsEnhanced || next.StreamType != 1 || next.SubstreamId != dependent ||
                    next.SampleRate != header.SampleRate || next.SamplesPerFrame != header.SamplesPerFrame)
                { throw new InvalidDataException("The E-AC-3 frame set no longer matches its index."); }
                dependent++; offset = checked(offset + next.FrameBytes);
            }
            if (offset != location.ByteLength) { throw new InvalidDataException("The AC-3 syncframe boundaries no longer match their index."); }
            token.ThrowIfCancellationRequested();
            using MediaBuffer buffer = writable.Publish();
            MediaTimestamp pts = Timestamp(index);
            return new(0, buffer, pts, pts, new(location.Samples, Streams[0].TimeBase), isKeyFrame: true);
        }

        private MediaTimestamp Timestamp(int index) => new(locations[index].FirstSample, Streams[0].TimeBase);
        private int RequireStream(int streamIndex)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (streamIndex != 0) { throw new ArgumentOutOfRangeException(nameof(streamIndex)); }
            return locations.Count;
        }
        private int RequirePacket(int streamIndex, long packetIndex)
        {
            int count = RequireStream(streamIndex);
            ArgumentOutOfRangeException.ThrowIfNegative(packetIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(packetIndex, count);
            return (int)packetIndex;
        }
        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            { throw new InvalidOperationException("Concurrent AC-3 reader operations are not supported."); }
        }
        private void Exit() => Volatile.Write(ref busy, 0);
        public ValueTask DisposeAsync()
        {
            Enter();
            try { disposed = true; locations.Clear(); locations.Capacity = 0; }
            finally { Exit(); }
            GC.SuppressFinalize(this); return ValueTask.CompletedTask;
        }
    }
}
