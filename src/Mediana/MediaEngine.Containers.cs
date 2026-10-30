// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Metadata;
using Mediana.Codecs;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana
{
    public partial class MediaEngine
    {
        /// <summary>Opens encoded packets using registered container probes, without creating a decoder.
        /// Borrows the source. Sequential probing is replayed; random access preserves the caller's cursor.</summary>
        public async ValueTask<ContainerSession> OpenContainerAsync(IByteSource source,
            MediaOpenOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            options ??= new MediaOpenOptions();
            options.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            if (containers.Length == 0)
            {
                throw new NotSupportedException("No container readers are registered.");
            }
            int probeLength = containers.Max(item => item.ProbeLength);
            if (probeLength > options.MaxProbeBytes)
            {
                throw new NotSupportedException("Registered container probing exceeds the configured probe limit.");
            }
            MediaBufferAllocator allocator = new(options.BufferBudgetBytes);
            using WritableMediaBuffer writable = allocator.Allocate(probeLength);
            int read = 0;
            while (read < probeLength)
            {
                int count = source is IRandomAccessByteSource randomSource
                    ? await randomSource.ReadAtAsync(read, writable.Memory[read..], cancellationToken).ConfigureAwait(false)
                    : await source.ReadAsync(writable.Memory[read..], cancellationToken).ConfigureAwait(false);
                if (count < 0 || count > probeLength - read)
                {
                    throw new InvalidDataException("The source returned an invalid read length.");
                }
                if (count == 0)
                {
                    break;
                }
                read += count;
            }
            cancellationToken.ThrowIfCancellationRequested();
            IContainerReaderFactory factory = SelectContainer(writable.Memory.Span[..read]);
            using MediaBuffer prefix = writable.Publish();
            IByteSource replay = source is IRandomAccessByteSource randomAccess
                ? new RandomAccessCursorByteSource(randomAccess, leaveOpen: true)
                : new PrefixByteSource(source, prefix, read, leaveOpen: true);
            IContainerReader? reader = null;
            bool transferred = false;
            try
            {
                reader = await factory.OpenAsync(replay, allocator, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                ContainerSession session = new(reader, replay, allocator, options.MaxPacketReads);
                transferred = true;
                return session;
            }
            finally
            {
                if (!transferred)
                {
                    try
                    {
                        if (reader is not null)
                        {
                            await reader.DisposeAsync().ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        await replay.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
        }

        /// <summary>Reads container/stream header metadata without decoding or scanning packets.
        /// Borrows the source and preserves a random access source's cursor. Returned metadata is independent of source lifetime.</summary>
        public async ValueTask<MediaInfo> ProbeAsync(IByteSource source, MediaOpenOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            await using ContainerSession session = await OpenContainerAsync(source, options, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            Dictionary<int, StreamMetadata> metadata = [];
            foreach (EncodedStreamInfo stream in session.Streams)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (metadataReaders.TryGetValue(stream.CodecId, out IStreamMetadataReader? reader))
                {
                    StreamMetadata? value = reader.Read(stream);
                    if (value is not null)
                    {
                        if (value.Kind != stream.Kind)
                        {
                            throw new InvalidOperationException("The metadata reader returned a different media kind.");
                        }
                        metadata.Add(stream.StreamIndex, value);
                    }
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new MediaInfo(session.Info, session.Streams, metadata);
        }

        private IContainerReaderFactory SelectContainer(ReadOnlySpan<byte> prefix)
        {
            IContainerReaderFactory? selected = null;
            foreach (IContainerReaderFactory factory in containers)
            {
                if (factory.CanRead(prefix))
                {
                    if (selected is not null)
                    {
                        throw new NotSupportedException("Multiple container readers match; register a single implementation for this input.");
                    }
                    selected = factory;
                }
            }
            return selected ?? throw new NotSupportedException("No registered container reader recognizes the input.");
        }

    }
}
