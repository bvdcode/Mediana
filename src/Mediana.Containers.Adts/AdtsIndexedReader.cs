// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Formats;
using Mediana.Formats.Audio;
using Mediana.IO;

namespace Mediana.Containers.Adts
{
    /// <summary>Indexes frame headers without reading compressed payloads. Borrows a complete immutable random-access source.
    /// Reads preserve the source cursor; indexed reads also preserve the reader cursor. CRC words are not verified.</summary>
    public partial class AdtsIndexedReader : IIndexedPrerollReader, IIndexedPacketTimingReader, IIndexedPresentationTimingReader
    {
        private readonly IRandomAccessByteSource source;
        private readonly MediaBufferAllocator allocator;
        private readonly List<AdtsFrameLocation> locations;
        private int cursor;
        private bool disposed;
        private int busy;

        private AdtsIndexedReader(IRandomAccessByteSource source, MediaBufferAllocator allocator,
            AdtsFrameHeader first, List<AdtsFrameLocation> locations)
        {
            this.source = source;
            this.allocator = allocator;
            this.locations = locations;
            FirstFrame = first;
            TimeBase clock = new(1, first.SampleRate);
            Streams = Array.AsReadOnly(new[] { new EncodedStreamInfo(0, MediaKind.Audio, AacConfiguration.CodecId,
                StreamFormats.Aac, clock, first.ToAudioSpecificConfig(),
                codedDuration: new MediaDuration((long)locations.Count * AdtsFrameValidation.SamplesPerFrame, clock)) });
        }

        public AdtsFrameHeader FirstFrame { get; }
        public ContainerInfo Info { get; } = new("adts");
        public IReadOnlyList<EncodedStreamInfo> Streams { get; }

        public static async ValueTask<AdtsIndexedReader> OpenAsync(IRandomAccessByteSource source,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(allocator);
            long length = source.Length ?? throw new NotSupportedException("ADTS indexing requires a known source length.");
            if (length < AdtsFrameHeader.ByteLength) { throw new InvalidDataException("The ADTS source is empty or truncated."); }
            cancellationToken.ThrowIfCancellationRequested();
            using WritableMediaBuffer scratch = allocator.Allocate(AdtsFrameHeader.ByteLength);
            List<AdtsFrameLocation> locations = [];
            AdtsFrameHeader first = default;
            long position = 0;
            while (position < length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (length - position < AdtsFrameHeader.ByteLength)
                {
                    throw new InvalidDataException("The final ADTS header is truncated.");
                }
                await source.ReadExactlyAtAsync(position, scratch.Memory, cancellationToken).ConfigureAwait(false);
                AdtsFrameHeader header = AdtsFrameHeader.Read(scratch.Memory.Span);
                if (locations.Count == 0) { first = header; }
                AdtsFrameValidation.RequireConfiguration(first, header);
                if (header.FrameBytes > length - position)
                {
                    throw new InvalidDataException("The ADTS frame extends beyond the source.");
                }
                locations.Add(new(position + header.HeaderBytes, header.FrameBytes - header.HeaderBytes));
                position = checked(position + header.FrameBytes);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new(source, allocator, first, locations);
        }

        public async ValueTask<MediaPacket?> ReadPacketAsync(CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                RequireActive();
                cancellationToken.ThrowIfCancellationRequested();
                if (cursor == locations.Count) { return null; }
                MediaPacket packet = await ReadCoreAsync(cursor, cancellationToken).ConfigureAwait(false);
                cursor++;
                return packet;
            }
            finally { Exit(); }
        }

        public async ValueTask<MediaPacket> ReadPacketAtAsync(int streamIndex, long packetIndex,
            CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                int index = RequirePacket(streamIndex, packetIndex);
                return await ReadCoreAsync(index, cancellationToken).ConfigureAwait(false);
            }
            finally { Exit(); }
        }

        private async ValueTask<MediaPacket> ReadCoreAsync(int index, CancellationToken token)
        {
            AdtsFrameLocation location = locations[index];
            using WritableMediaBuffer writable = allocator.Allocate(location.ByteLength);
            await source.ReadExactlyAtAsync(location.Offset, writable.Memory, token).ConfigureAwait(false);
            using MediaBuffer buffer = writable.Publish();
            MediaTimestamp timestamp = Timestamp(index);
            return new(0, buffer, timestamp, timestamp, Duration);
        }

        private MediaTimestamp Timestamp(int index) => new((long)index * AdtsFrameValidation.SamplesPerFrame, Streams[0].TimeBase);
        private MediaDuration Duration => new(AdtsFrameValidation.SamplesPerFrame, Streams[0].TimeBase);

        private void RequireActive() => ObjectDisposedException.ThrowIf(disposed, this);

        private int RequireStream(int streamIndex)
        {
            RequireActive();
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
            {
                throw new InvalidOperationException("Concurrent ADTS reader operations are not supported.");
            }
        }

        private void Exit() => Volatile.Write(ref busy, 0);

        public ValueTask DisposeAsync()
        {
            Enter();
            try { disposed = true; locations.Clear(); locations.Capacity = 0; }
            finally { Exit(); }
            GC.SuppressFinalize(this);
            return ValueTask.CompletedTask;
        }
    }
}
