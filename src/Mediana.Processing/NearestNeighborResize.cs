using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Processing
{
    public static class NearestNeighborResize
    {
        /// <summary>Resizes RGBA32 storage without changing its color encoding. Borrows input; returns an owned frame.</summary>
        public static VideoFrame Apply(VideoFrame input, int width, int height, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            NearestNeighborMap map = new(input.Format.Width, input.Format.Height, width, height, OrthogonalTransform.Identity);
            return Apply(input, map, input.Format.SampleAspectRatio, allocator, cancellationToken);
        }

        internal static VideoFrame Apply(VideoFrame input, NearestNeighborMap map, SampleAspectRatio? outputAspectRatio,
            MediaBufferAllocator allocator, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(allocator);
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(input.Format.PixelFormat, VideoPixelFormat.Rgba32))
            {
                throw new NotSupportedException("This resize implementation supports the RGBA32 pixel format only.");
            }
            int outputWidth = map.OutputWidth;
            int outputHeight = map.OutputHeight;
            VideoFormat format = new(outputWidth, outputHeight, VideoPixelFormat.Rgba32, input.Format.Color, outputAspectRatio);
            PlaneLayout[] planes = format.CreateTightLayout();
            using WritableMediaBuffer writable = allocator.Allocate(checked((int)planes[0].EndOffset));
            for (int y = 0; y < outputHeight; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int cachedRow = -1;
                ReadOnlySpan<byte> source = default;
                Span<byte> destination = writable.Memory.Span.Slice(y * planes[0].Stride, planes[0].RowBytes);
                for (int x = 0; x < outputWidth; x++)
                {
                    if ((x & 255) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    int sourceX = map.SourceX(x, y);
                    int sourceY = map.SourceY(x, y);
                    if (sourceY != cachedRow)
                    {
                        source = input.GetRow(0, sourceY);
                        cachedRow = sourceY;
                    }
                    source.Slice(sourceX * 4, 4).CopyTo(destination.Slice(x * 4, 4));
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            using MediaBuffer buffer = writable.Publish();
            return new VideoFrame(format, buffer, planes, input.Timestamp, input.Duration);
        }
    }
}
