// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Containers.Wave
{
    /// <summary>Reads one RIFF/WAVE PCM data chunk sequentially. Borrows its source.
    /// Trailing chunks and padding are validated when the packet stream is drained.
    /// RF64, RIFX, compressed WAVE and nested audio lists are not supported.</summary>
    public class WaveReader : IContainerReader
    {
        private const uint FormatChunk = 0x20746d66;
        private const uint DataChunk = 0x61746164;
        private const uint ListChunk = 0x5453494c;
        private const int ReadBufferBytes = 4096;
        private readonly IByteSource source;
        private readonly MediaBufferAllocator allocator;
        private readonly WaveReadOptions options;
        private readonly WritableMediaBuffer scratch;
        private long remaining;
        private long metadataBytes;
        private long dataRemaining;
        private long samplePosition;
        private bool dataPadding;
        private int chunks;
        private bool disposed;
        private bool faulted;
        private int busy;

        private WaveReader(IByteSource source, MediaBufferAllocator allocator, WaveReadOptions options)
        {
            this.source = source;
            this.allocator = allocator;
            this.options = options;
            scratch = allocator.Allocate(ReadBufferBytes);
        }

        public IReadOnlyList<EncodedStreamInfo> Streams { get; private set; } = [];
        public PcmConfiguration Configuration { get; private set; } = null!;
        public long SamplesPerChannel { get; private set; }
        public MediaDuration Duration => new(SamplesPerChannel, new TimeBase(1, Configuration.Format.SampleRate));

        public static async ValueTask<WaveReader> OpenAsync(IByteSource source, MediaBufferAllocator allocator,
            WaveReadOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(allocator);
            options ??= new WaveReadOptions();
            options.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            WaveReader reader = new(source, allocator, options);
            bool opened = false;
            try
            {
                await reader.InitializeAsync(cancellationToken).ConfigureAwait(false);
                opened = true;
                return reader;
            }
            finally
            {
                if (!opened)
                {
                    await reader.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        private async ValueTask InitializeAsync(CancellationToken cancellationToken)
        {
            await source.ReadExactlyAsync(scratch.Memory[..12], cancellationToken).ConfigureAwait(false);
            if (!scratch.Memory.Span[..4].SequenceEqual("RIFF"u8) || !scratch.Memory.Span.Slice(8, 4).SequenceEqual("WAVE"u8))
            {
                throw new NotSupportedException("The source is not a RIFF/WAVE file.");
            }
            long size = BinaryPrimitives.ReadUInt32LittleEndian(scratch.Memory.Span[4..]);
            if (size < 4 || (source.Length is long length && size > length - 8))
            {
                throw new InvalidDataException("The RIFF/WAVE extent exceeds the source.");
            }
            remaining = size - 4;
            CountMetadata(12);
            bool formatRead = false;
            while (remaining != 0)
            {
                (uint id, uint chunkSize) = await ReadChunkAsync(cancellationToken).ConfigureAwait(false);
                switch (id)
                {
                    case FormatChunk:
                        if (formatRead || chunkSize < 16)
                        {
                            throw new InvalidDataException("The WAVE format chunk is duplicated or truncated.");
                        }
                        if (chunkSize > options.MaxFormatBytes)
                        {
                            throw new NotSupportedException("The WAVE format chunk exceeds its configured size.");
                        }
                        CountMetadata(chunkSize);
                        using (WritableMediaBuffer format = allocator.Allocate((int)chunkSize))
                        {
                            await ReadAsync(format.Memory, cancellationToken).ConfigureAwait(false);
                            Configuration = PcmConfiguration.ReadWaveFormat(format.Memory.Span, out string? reason)
                                ?? throw new NotSupportedException(reason);
                            Streams = Array.AsReadOnly(new[] { new EncodedStreamInfo(0, MediaKind.Audio, PcmConfiguration.CodecId,
                                new TimeBase(1, Configuration.Format.SampleRate), format.Memory.Span) });
                        }
                        formatRead = true;
                        await SkipMetadataAsync(chunkSize & 1, cancellationToken).ConfigureAwait(false);
                        break;
                    case DataChunk:
                        if (!formatRead || chunkSize % Configuration.BlockAlignment != 0)
                        {
                            throw new InvalidDataException("WAVE data requires an earlier format and complete PCM blocks.");
                        }
                        dataRemaining = chunkSize;
                        dataPadding = (chunkSize & 1) != 0;
                        SamplesPerChannel = chunkSize / Configuration.BlockAlignment;
                        return;
                    default:
                        await SkipChunkAsync(id, chunkSize, cancellationToken).ConfigureAwait(false);
                        break;
                }
            }
            throw new InvalidDataException("The WAVE file has no data chunk.");
        }

        public async ValueTask<MediaPacket?> ReadPacketAsync(CancellationToken cancellationToken = default)
        {
            Enter();
            bool succeeded = false;
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (faulted)
                {
                    throw new InvalidOperationException("The WAVE reader has failed and must be disposed.");
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (dataRemaining == 0)
                {
                    await DrainTailAsync(cancellationToken).ConfigureAwait(false);
                    succeeded = true;
                    return null;
                }
                int samples = (int)Math.Min(options.SamplesPerPacket, dataRemaining / Configuration.BlockAlignment);
                int bytes = checked(samples * Configuration.BlockAlignment);
                using WritableMediaBuffer writable = allocator.Allocate(bytes);
                await ReadAsync(writable.Memory, cancellationToken).ConfigureAwait(false);
                TimeBase timeBase = Streams[0].TimeBase;
                using MediaBuffer buffer = writable.Publish();
                MediaPacket packet = new(0, buffer, new MediaTimestamp(samplePosition, timeBase), new MediaTimestamp(samplePosition, timeBase),
                    new MediaDuration(samples, timeBase), isKeyFrame: true);
                samplePosition += samples;
                dataRemaining -= bytes;
                succeeded = true;
                return packet;
            }
            finally
            {
                if (!succeeded)
                {
                    faulted = true;
                    scratch.Dispose();
                }
                Volatile.Write(ref busy, 0);
            }
        }

        private async ValueTask DrainTailAsync(CancellationToken cancellationToken)
        {
            if (dataPadding)
            {
                await SkipMetadataAsync(1, cancellationToken).ConfigureAwait(false);
                dataPadding = false;
            }
            while (remaining != 0)
            {
                (uint id, uint size) = await ReadChunkAsync(cancellationToken).ConfigureAwait(false);
                if (id is DataChunk or FormatChunk)
                {
                    throw new InvalidDataException("Multiple WAVE format or data chunks are not supported.");
                }
                await SkipChunkAsync(id, size, cancellationToken).ConfigureAwait(false);
            }
        }

        private async ValueTask SkipChunkAsync(uint id, uint size, CancellationToken cancellationToken)
        {
            long bytes = (long)size + (size & 1);
            if (id == ListChunk)
            {
                if (size < 4)
                {
                    throw new InvalidDataException("A RIFF LIST chunk requires a list type.");
                }
                CountMetadata(4);
                await ReadAsync(scratch.Memory[..4], cancellationToken).ConfigureAwait(false);
                if (scratch.Memory.Span[..4].SequenceEqual("wavl"u8))
                {
                    throw new NotSupportedException("Nested WAVE audio and silence lists are not supported.");
                }
                bytes -= 4;
            }
            await SkipMetadataAsync(bytes, cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask<(uint Id, uint Size)> ReadChunkAsync(CancellationToken cancellationToken)
        {
            if (chunks == options.MaxChunks)
            {
                throw new NotSupportedException("The WAVE chunk count exceeds the configured limit.");
            }
            chunks++;
            CountMetadata(8);
            await ReadAsync(scratch.Memory[..8], cancellationToken).ConfigureAwait(false);
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(scratch.Memory.Span);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(scratch.Memory.Span[4..]);
            if ((long)size + (size & 1) > remaining)
            {
                throw new InvalidDataException("A WAVE chunk exceeds its RIFF extent.");
            }
            return (id, size);
        }

        private async ValueTask ReadAsync(Memory<byte> destination, CancellationToken cancellationToken)
        {
            if (destination.Length > remaining)
            {
                throw new InvalidDataException("WAVE data exceeds its RIFF extent.");
            }
            await source.ReadExactlyAsync(destination, cancellationToken).ConfigureAwait(false);
            remaining -= destination.Length;
        }

        private void CountMetadata(long length)
        {
            if (length > options.MaxMetadataBytes - metadataBytes)
            {
                throw new NotSupportedException("WAVE metadata exceeds the configured byte limit.");
            }
            metadataBytes += length;
        }

        private async ValueTask SkipMetadataAsync(long length, CancellationToken cancellationToken)
        {
            CountMetadata(length);
            while (length != 0)
            {
                int bytes = (int)Math.Min(length, scratch.Memory.Length);
                await ReadAsync(scratch.Memory[..bytes], cancellationToken).ConfigureAwait(false);
                length -= bytes;
            }
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("Concurrent WAVE reader operations are not supported.");
            }
        }

        public ValueTask DisposeAsync()
        {
            Enter();
            try
            {
                if (!disposed)
                {
                    disposed = true;
                    scratch.Dispose();
                }
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
            GC.SuppressFinalize(this);
            return ValueTask.CompletedTask;
        }
    }
}
