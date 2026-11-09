// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana
{
    public partial class MediaEngine
    {
        /// <summary>Opens encoded packets using registered container probes, without creating a decoder.
        /// Borrows the source. Sequential probing is replayed; random access preserves the caller's cursor.</summary>
        public ValueTask<ContainerSession> OpenContainerAsync(IByteSource source,
            MediaOpenOptions? options = null, CancellationToken cancellationToken = default)
        {
            options ??= new MediaOpenOptions();
            options.Validate();
            return OpenContainerCoreAsync(source, new(options.BufferBudgetBytes), options, cancellationToken);
        }

        /// <summary>Opens a container using a scope of the caller's allocator for probing, index and packet storage.
        /// Borrows source and allocator; the input budget also counts against the caller's shared budget.</summary>
        public ValueTask<ContainerSession> OpenContainerAsync(IByteSource source, MediaBufferAllocator allocator,
            MediaOpenOptions options, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            ArgumentNullException.ThrowIfNull(options);
            options.Validate();
            return OpenContainerCoreAsync(source, allocator.CreateScope(options.BufferBudgetBytes), options, cancellationToken);
        }

        private async ValueTask<ContainerSession> OpenContainerCoreAsync(IByteSource source, MediaBufferAllocator allocator,
            MediaOpenOptions options, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(source);
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
            using WritableMediaBuffer writable = allocator.Allocate(probeLength);
            int read = await MediaInputProbe.ReadAsync(source, writable.Memory, cancellationToken).ConfigureAwait(false);
            IContainerReaderFactory factory = SelectContainer(writable.Memory.Span[..read]);
            using MediaBuffer prefix = writable.Publish();
            IByteSource replay = MediaInputProbe.Replay(source, prefix, read);
            IContainerReader? reader = null;
            bool transferred = false;
            try
            {
                reader = await factory.OpenAsync(replay, allocator, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                ContainerSession session = new(reader, replay, allocator, options);
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

        private IContainerReaderFactory SelectContainer(ReadOnlySpan<byte> prefix) => FindContainer(prefix)
            ?? throw new NotSupportedException("No registered container reader recognizes the input.");

        private IContainerReaderFactory? FindContainer(ReadOnlySpan<byte> prefix)
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
            return selected;
        }

    }
}
