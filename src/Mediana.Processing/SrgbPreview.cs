using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Processing
{
    public static class SrgbPreview
    {
        /// <summary>Fits supported SDR YUV420P8 or straight RGBA32 to a square-pixel sRGB preview in one pass.
        /// Uses the same nearest-neighbor grid as DisplayResize after SrgbConverter, including SAR and exact
        /// orthogonal rotation/reflection. Converts only selected pixels without a full-size RGB buffer.
        /// Requires known color and sample aspect ratio. Borrows input; returns an independent owned frame.</summary>
        public static VideoFrame Fit(VideoFrame input, VideoDisplayMatrix displayMatrix, int maximumWidth, int maximumHeight,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(allocator);
            cancellationToken.ThrowIfCancellationRequested();
            SampleAspectRatio aspectRatio = input.Format.SampleAspectRatio
                ?? throw new NotSupportedException("A preview requires a known sample aspect ratio.");
            OrthogonalTransform orientation = OrthogonalTransform.FromMatrix(displayMatrix);
            NearestNeighborMap map = NearestNeighborMap.Fit(input.Format, aspectRatio, orientation, maximumWidth, maximumHeight);
            return SrgbConverter.Apply(input, input.Format.Color, map, SampleAspectRatio.Square, allocator, cancellationToken);
        }
    }
}
