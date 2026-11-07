// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Processing
{
    /// <summary>Configured native-YUV preparation shared by file encoding and segmented output.
    /// Applies display orientation, explicit precision conversion and optional area scaling.
    /// Borrows the allocator and input frames; returned frames own independent leases.
    /// Calls and disposal must not overlap.</summary>
    public class VideoPreparation : IDisposable
    {
        private readonly Yuv420FrameTransform transform;
        private readonly Yuv420AreaResizer? resizer;
        private bool disposed;
        private int busy;

        public VideoPreparation(VideoFormat inputFormat, VideoDisplayMatrix sourceDisplayMatrix,
            MediaBufferAllocator allocator, VideoPreparationOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(inputFormat);
            ArgumentNullException.ThrowIfNull(allocator);
            options ??= new();
            if (options.MaximumWidth is < 2 || options.MaximumHeight is < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "Video dimension bounds must be at least two samples.");
            }
            transform = new(inputFormat, sourceDisplayMatrix, options.OutputPixelFormat ?? inputFormat.PixelFormat, allocator);
            if (options.MaximumWidth is not null || options.MaximumHeight is not null)
            {
                resizer = Yuv420AreaResizer.CreateFit(transform.OutputFormat,
                    options.MaximumWidth ?? transform.OutputFormat.Width,
                    options.MaximumHeight ?? transform.OutputFormat.Height, allocator);
            }
            OutputFormat = resizer?.OutputFormat ?? transform.OutputFormat;
        }

        public VideoFormat OutputFormat { get; }

        public VideoFrame Apply(VideoFrame input, CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (resizer is null) { return transform.Apply(input, cancellationToken); }
                using VideoFrame transformed = transform.Apply(input, cancellationToken);
                return resizer.ResizeOrRetain(transformed, cancellationToken);
            }
            finally { Volatile.Write(ref busy, 0); }
        }

        public void Dispose()
        {
            Enter();
            try
            {
                disposed = true;
                resizer?.Dispose();
            }
            finally { Volatile.Write(ref busy, 0); }
            GC.SuppressFinalize(this);
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("Concurrent video preparation operations are not supported.");
            }
        }
    }
}
