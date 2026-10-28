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
            return Apply(input, width, height, input.Format.SampleAspectRatio, allocator, cancellationToken);
        }

        internal static VideoFrame Apply(VideoFrame input, int width, int height, SampleAspectRatio? outputAspectRatio,
            MediaBufferAllocator allocator, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(allocator);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(input.Format.PixelFormat, VideoPixelFormat.Rgba32))
            {
                throw new NotSupportedException("This resize implementation supports the RGBA32 pixel format only.");
            }
            VideoFormat format = new(width, height, VideoPixelFormat.Rgba32, input.Format.Color, outputAspectRatio);
            PlaneLayout[] planes = format.CreateTightLayout();
            using WritableMediaBuffer writable = allocator.Allocate(checked((int)planes[0].EndOffset));
            for (int y = 0; y < height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int sourceY = (int)((long)y * input.Format.Height / height);
                ReadOnlySpan<byte> source = input.GetRow(0, sourceY);
                Span<byte> destination = writable.Memory.Span.Slice(y * planes[0].Stride, planes[0].RowBytes);
                for (int x = 0; x < width; x++)
                {
                    if ((x & 255) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    int sourceX = (int)((long)x * input.Format.Width / width);
                    source.Slice(sourceX * 4, 4).CopyTo(destination.Slice(x * 4, 4));
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            using MediaBuffer buffer = writable.Publish();
            return new VideoFrame(format, buffer, planes, input.Timestamp, input.Duration);
        }
    }
}
