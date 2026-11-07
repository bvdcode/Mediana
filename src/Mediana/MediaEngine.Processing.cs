// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core.Memory;
using Mediana.Execution;
using Mediana.IO;

namespace Mediana
{
    public partial class MediaEngine
    {
        /// <summary>Prepares independently indexed track copies/encoders for a whole input or selected presentation interval.
        /// Borrows reader and allocator. Uses the reader's declared timeline without implicit timestamp rebasing.
        /// Factories carry codec settings; compressed copies preserve encoded samples and do not apply container edits.
        /// Decoder storage respects the configured local input budget; preparation and encoding use the output allocator.</summary>
        public ValueTask<MediaProcessingSession> OpenProcessingAsync(IIndexedContainerReader reader,
            IReadOnlyList<MediaTrackRequest> tracks, MediaBufferAllocator allocator,
            MediaProcessingOptions? options = null, CancellationToken cancellationToken = default) =>
            OpenProcessingCoreAsync(reader, tracks, allocator, null, options, cancellationToken);

        private async ValueTask<MediaProcessingSession> OpenProcessingCoreAsync(IIndexedContainerReader reader,
            IReadOnlyList<MediaTrackRequest> tracks, MediaBufferAllocator allocator, MediaBufferAllocator? inputAllocator,
            MediaProcessingOptions? options, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(reader);
            ArgumentNullException.ThrowIfNull(tracks);
            ArgumentNullException.ThrowIfNull(allocator);
            MediaTrackRequest[] selected = tracks.ToArray();
            if (selected.Length == 0 || selected.Any(track => track is null) ||
                selected.Select(track => track.OutputStreamIndex).Distinct().Count() != selected.Length)
            { throw new ArgumentException("Select nonnull tracks with unique output stream indices.", nameof(tracks)); }
            options ??= new();
            options.Input.Validate();
            if (options.Selection is Core.Timing.MediaTimeRange range)
            { range.Start.TimeBase.Validate(); range.End.TimeBase.Validate(); }
            cancellationToken.ThrowIfCancellationRequested();
            MediaTrackResources resources = new();
            bool success = false;
            try
            {
                MediaReadBudget budget = new(options.Input.MaxPacketReads, options.Input.MaxFrameReads);
                MediaTrackInput input = new(reader, allocator, inputAllocator ?? allocator.CreateScope(options.Input.BufferBudgetBytes),
                    budget, resources, options.Selection);
                foreach (MediaTrackRequest track in selected) { _ = input.Select(track.InputStreamIndex); }
                ProcessedMediaTrack[] sources = new ProcessedMediaTrack[selected.Length];
                for (int index = 0; index < selected.Length; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    sources[index] = resources.OwnAsync(await selected[index].OpenAsync(this, input, cancellationToken).ConfigureAwait(false));
                }
                cancellationToken.ThrowIfCancellationRequested();
                MediaProcessingSession result = new(sources, resources);
                success = true;
                return result;
            }
            finally { if (!success) { await resources.DisposeAsync().ConfigureAwait(false); } }
        }

        /// <summary>Reads one indexed container and muxes explicit copied or encoded audio/video tracks.
        /// Borrows source, destination and output allocator. No complete compressed or decoded file is retained.
        /// Sequential-only containers require a sequential processing operation.</summary>
        public async Task<MediaProcessingResult> ProcessAsync(IByteSource source, Stream destination,
            string containerFormatId, IReadOnlyList<MediaTrackRequest> tracks, MediaBufferAllocator allocator,
            MediaProcessingOptions? options = null, CancellationToken cancellationToken = default)
        {
            ValidateDestination(destination);
            ArgumentNullException.ThrowIfNull(allocator);
            options ??= new();
            await using ContainerSession container = await OpenContainerAsync(source, allocator, options.Input, cancellationToken).ConfigureAwait(false);
            if (container.Reader is not IIndexedContainerReader indexed)
            { throw new NotSupportedException("Independent track processing requires an indexed container reader."); }
            await using MediaProcessingSession session = await OpenProcessingCoreAsync(indexed, tracks, allocator, container.Allocator, options, cancellationToken).ConfigureAwait(false);
            await using IContainerWriter writer = CreateContainerWriter(containerFormatId, destination, session.Streams, allocator);
            return await session.WriteAsync(writer, cancellationToken).ConfigureAwait(false);
        }
    }
}
