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
        private readonly RgbaToYuv420? rgb;
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
            VideoFormat planar = inputFormat;
            if ((inputFormat.PixelFormat == VideoPixelFormat.Rgba32 || inputFormat.PixelFormat is PlanarVideoPixelFormat { ColorModel: PlanarColorModel.Gbr }) && options.RgbConversion is not null)
            {
                rgb = new(inputFormat, options.RgbConversion, allocator);
                planar = rgb.OutputFormat;
            }
            transform = new(planar, sourceDisplayMatrix, options.OutputPixelFormat ?? DefaultOutput(planar.PixelFormat), allocator);
            if (options.MaximumWidth is not null || options.MaximumHeight is not null)
            {
                resizer = Yuv420AreaResizer.CreateFit(transform.OutputFormat,
                    options.MaximumWidth ?? transform.OutputFormat.Width,
                    options.MaximumHeight ?? transform.OutputFormat.Height, allocator);
            }
            OutputFormat = resizer?.OutputFormat ?? transform.OutputFormat;
        }

        private static VideoPixelFormat DefaultOutput(VideoPixelFormat input)
        {
            if (input is not PlanarVideoPixelFormat planar || planar.ColorModel != PlanarColorModel.Yuv ||
                planar.HorizontalSubsampling == 2 && planar.VerticalSubsampling == 2) { return input; }
            return planar.BitDepth switch
            {
                8 => VideoPixelFormat.Yuv420P8,
                10 => VideoPixelFormat.Yuv420P10,
                12 => VideoPixelFormat.Yuv420P12,
                _ => new PlanarVideoPixelFormat($"yuv420p{planar.BitDepth}", PlanarColorModel.Yuv, planar.BitDepth, 2, 2)
            };
        }

        public VideoFormat OutputFormat { get; }

        public VideoFrame Apply(VideoFrame input, CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                using VideoFrame? converted = rgb?.Apply(input, cancellationToken);
                if (resizer is null) { return transform.Apply(converted ?? input, cancellationToken); }
                using VideoFrame transformed = transform.Apply(converted ?? input, cancellationToken);
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
