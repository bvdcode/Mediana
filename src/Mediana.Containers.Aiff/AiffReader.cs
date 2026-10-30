// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Containers.Aiff
{
    /// <summary>Sequential mono/stereo PCM reader for AIFF and AIFF-C. Borrows its source.
    /// COMM and the AIFF-C FVER chunk must precede SSND. Trailing chunks are validated on drain.</summary>
    public partial class AiffReader : IContainerReader
    {
        private const uint CommonChunk = 0x434f4d4d;
        private const uint SoundChunk = 0x53534e44;
        private const uint VersionChunk = 0x46564552;
        private const uint AifcVersion = 0xa2805140;
        private const int ScratchBytes = 4096;
        private readonly IByteSource source;
        private readonly MediaBufferAllocator allocator;
        private readonly AiffReadOptions options;
        private readonly WritableMediaBuffer scratch;
        private long remaining;
        private long metadataBytes;
        private long soundRemaining;
        private long soundTail;
        private long samplePosition;
        private uint soundChunkSize;
        private int chunks;
        private int busy;
        private bool compressed;
        private bool versionRead;
        private bool disposed;
        private bool faulted;

        private AiffReader(IByteSource source, MediaBufferAllocator allocator, AiffReadOptions options)
        {
            this.source = source;
            this.allocator = allocator;
            this.options = options;
            scratch = allocator.Allocate(ScratchBytes);
        }

        public IReadOnlyList<EncodedStreamInfo> Streams { get; private set; } = [];
        public PcmConfiguration Configuration { get; private set; } = null!;
        public long SamplesPerChannel { get; private set; }
        public MediaDuration Duration => new(SamplesPerChannel, new TimeBase(1, Configuration.Format.SampleRate));

        public static async ValueTask<AiffReader> OpenAsync(IByteSource source, MediaBufferAllocator allocator,
            AiffReadOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(allocator);
            options ??= new AiffReadOptions();
            options.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            AiffReader reader = new(source, allocator, options);
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
            if (!AiffReaderFactory.HasSignature(scratch.Memory.Span[..12]))
            {
                throw new NotSupportedException("The source is not an AIFF or AIFF-C file.");
            }
            compressed = scratch.Memory.Span.Slice(8, 4).SequenceEqual("AIFC"u8);
            long size = BinaryPrimitives.ReadUInt32BigEndian(scratch.Memory.Span[4..]);
            if (size < 4 || (source.Length is long length && size > length - 8))
            {
                throw new InvalidDataException("The AIFF FORM extent exceeds the source.");
            }
            remaining = size - 4;
            CountMetadata(12);
            while (remaining != 0)
            {
                (uint id, uint chunkSize) = await ReadChunkAsync(cancellationToken).ConfigureAwait(false);
                switch (id)
                {
                    case CommonChunk:
                        await ReadCommonAsync(chunkSize, cancellationToken).ConfigureAwait(false);
                        break;
                    case VersionChunk when compressed:
                        await ReadVersionAsync(chunkSize, cancellationToken).ConfigureAwait(false);
                        break;
                    case SoundChunk:
                        await ReadSoundHeaderAsync(chunkSize, cancellationToken).ConfigureAwait(false);
                        return;
                    default:
                        await SkipMetadataAsync(chunkSize, cancellationToken).ConfigureAwait(false);
                        await ReadPaddingAsync(chunkSize, cancellationToken).ConfigureAwait(false);
                        break;
                }
            }
            if (Streams.Count == 0 || SamplesPerChannel != 0 || (compressed && !versionRead))
            {
                throw new InvalidDataException("The AIFF file is missing a required common, sound or version chunk.");
            }
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
                    throw new InvalidOperationException("The AIFF reader has failed and must be disposed.");
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (soundRemaining == 0)
                {
                    await DrainTailAsync(cancellationToken).ConfigureAwait(false);
                    succeeded = true;
                    return null;
                }
                int samples = (int)Math.Min(options.SamplesPerPacket, soundRemaining / Configuration.BlockAlignment);
                int bytes = checked(samples * Configuration.BlockAlignment);
                using WritableMediaBuffer writable = allocator.Allocate(bytes);
                await ReadAsync(writable.Memory, cancellationToken).ConfigureAwait(false);
                using MediaBuffer buffer = writable.Publish();
                TimeBase timeBase = Streams[0].TimeBase;
                MediaPacket packet = new(0, buffer, new MediaTimestamp(samplePosition, timeBase),
                    new MediaTimestamp(samplePosition, timeBase), new MediaDuration(samples, timeBase), isKeyFrame: true);
                samplePosition += samples;
                soundRemaining -= bytes;
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
            await SkipMetadataAsync(soundTail, cancellationToken).ConfigureAwait(false);
            soundTail = 0;
            await ReadPaddingAsync(soundChunkSize, cancellationToken).ConfigureAwait(false);
            soundChunkSize = 0;
            while (remaining != 0)
            {
                (uint id, uint size) = await ReadChunkAsync(cancellationToken).ConfigureAwait(false);
                if (id is CommonChunk or SoundChunk || (compressed && id == VersionChunk))
                {
                    throw new InvalidDataException("An AIFF common, sound or version chunk is duplicated.");
                }
                await SkipMetadataAsync(size, cancellationToken).ConfigureAwait(false);
                await ReadPaddingAsync(size, cancellationToken).ConfigureAwait(false);
            }
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("Concurrent AIFF reader operations are not supported.");
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
