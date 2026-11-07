// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Processing
{
    /// <summary>Prepares a frame sequence for one fixed encoder format. Reconfigures source processing when
    /// the decoded format changes, provided the configured output geometry and color interpretation remain equal.
    /// Owns only the active preparation; borrows frames and allocator. Calls and disposal must not overlap.</summary>
    public class VideoPreparationSequence : IDisposable
    {
        private readonly VideoDisplayMatrix displayMatrix;
        private readonly MediaBufferAllocator allocator;
        private readonly VideoPreparationOptions? options;
        private VideoFormat inputFormat;
        private VideoPreparation current;
        private bool disposed;
        private int busy;

        public VideoPreparationSequence(VideoFormat inputFormat, VideoDisplayMatrix displayMatrix,
            MediaBufferAllocator allocator, VideoPreparationOptions? options = null)
        {
            this.inputFormat = inputFormat; this.displayMatrix = displayMatrix; this.allocator = allocator; this.options = options;
            current = new(inputFormat, displayMatrix, allocator, options); OutputFormat = current.OutputFormat;
        }

        public VideoFormat OutputFormat { get; }

        public VideoFrame Apply(VideoFrame input, CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this); ArgumentNullException.ThrowIfNull(input); cancellationToken.ThrowIfCancellationRequested();
                if (!Matches(input.Format, inputFormat))
                {
                    VideoPreparation replacement = new(input.Format, displayMatrix, allocator, options); bool accepted = false;
                    try
                    {
                        if (!Matches(replacement.OutputFormat, OutputFormat))
                        { throw new NotSupportedException("The changed video source requires a different encoded output format; configure a common output geometry and color interpretation."); }
                        current.Dispose(); current = replacement; inputFormat = input.Format; accepted = true;
                    }
                    finally { if (!accepted) { replacement.Dispose(); } }
                }
                VideoFrame prepared = current.Apply(input, cancellationToken);
                if (ReferenceEquals(prepared.Format, OutputFormat)) { return prepared; }
                using (prepared) { return prepared.WithFormat(OutputFormat); }
            }
            finally { Volatile.Write(ref busy, 0); }
        }

        private static bool Matches(VideoFormat left, VideoFormat right) => left.Width == right.Width && left.Height == right.Height &&
            left.PixelFormat == right.PixelFormat && left.SampleAspectRatio == right.SampleAspectRatio &&
            left.GetColorInterpretation() == right.GetColorInterpretation();

        public void Dispose()
        {
            Enter(); try { disposed = true; current.Dispose(); } finally { Volatile.Write(ref busy, 0); }
            GC.SuppressFinalize(this);
        }
        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) { throw new InvalidOperationException("Concurrent video sequence preparation is not supported."); }
        }
    }
}
