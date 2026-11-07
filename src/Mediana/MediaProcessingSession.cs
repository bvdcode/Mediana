// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core;
using Mediana.Execution;

namespace Mediana
{
    /// <summary>Owns independent selected-track processing and borrows its indexed reader.
    /// Writes once, merging decode times with at most one pending packet per track.
    /// Codec memory and decoded lookahead remain bounded by the supplied allocator. Calls must not overlap.</summary>
    public class MediaProcessingSession : IAsyncDisposable
    {
        private readonly ProcessedMediaTrack[] tracks;
        private readonly MediaTrackResources resources;
        private bool written;
        private bool disposed;
        private int busy;

        internal MediaProcessingSession(ProcessedMediaTrack[] tracks, MediaTrackResources resources)
        {
            this.tracks = tracks;
            this.resources = resources;
            Streams = Array.AsReadOnly(tracks.Select(track => track.Stream).ToArray());
        }

        public IReadOnlyList<EncodedStreamInfo> Streams { get; }

        /// <summary>Borrows and completes the writer on success. Failure leaves unfinished output for the caller to discard.</summary>
        public Task<MediaProcessingResult> WriteAsync(IContainerWriter writer, CancellationToken cancellationToken = default) =>
            WriteAsync(writer, null, cancellationToken);

        /// <summary>Borrows the writer and observes each successfully written packet during the callback only.
        /// Completes the writer on success. Failure or cancellation leaves unfinished output for the caller to discard.</summary>
        public async Task<MediaProcessingResult> WriteAsync(IContainerWriter writer, Action<MediaPacket>? packetWritten,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(writer);
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (written) { throw new InvalidOperationException("The processing session has already been consumed."); }
                written = true;
                long packets = await MediaPacketMerge.WriteAsync(tracks, writer, packetWritten, cancellationToken).ConfigureAwait(false);
                return new(packets, Array.AsReadOnly(tracks.Select(track => track.Result).ToArray()));
            }
            finally { Volatile.Write(ref busy, 0); }
        }

        internal MediaOperationProgress GetSingleTrackProgress(bool complete)
        {
            if (tracks.Length != 1) { throw new InvalidOperationException("Single-track progress requires exactly one selected track."); }
            return tracks[0].GetProgress(complete);
        }

        public async ValueTask DisposeAsync()
        {
            Enter();
            try
            {
                if (!disposed) { disposed = true; await resources.DisposeAsync().ConfigureAwait(false); }
            }
            finally { Volatile.Write(ref busy, 0); GC.SuppressFinalize(this); }
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            { throw new InvalidOperationException("Processing session operations must not overlap."); }
        }
    }
}
