using System.Numerics;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Processing
{
    public static class DisplayResize
    {
        /// <summary>Fits RGBA32 into a box with square output pixels using nearest-neighbor sampling.
        /// The unscaled display size is storage width times SAR, by storage height. It is never enlarged.
        /// Dimensions are rounded to the nearest pixel, with a minimum of one. Borrows input; returns an owned frame.</summary>
        public static VideoFrame Fit(VideoFrame input, int maximumWidth, int maximumHeight, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            cancellationToken.ThrowIfCancellationRequested();
            SampleAspectRatio aspectRatio = input.Format.SampleAspectRatio
                ?? throw new NotSupportedException("Display resizing requires a known sample aspect ratio or an explicit interpretation.");
            return Fit(input, aspectRatio, maximumWidth, maximumHeight, allocator, cancellationToken);
        }

        /// <summary>Fits using an explicit interpretation of the source sample aspect ratio, without changing input metadata.</summary>
        public static VideoFrame Fit(VideoFrame input, SampleAspectRatio sourceAspectRatio, int maximumWidth, int maximumHeight,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(allocator);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumWidth);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumHeight);
            sourceAspectRatio.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            BigInteger displayWidth = (BigInteger)input.Format.Width * sourceAspectRatio.Numerator;
            BigInteger displayHeight = (BigInteger)input.Format.Height * sourceAspectRatio.Denominator;
            int boxWidth = RoundWithin(displayWidth, sourceAspectRatio.Denominator, maximumWidth);
            int boxHeight = Math.Min(input.Format.Height, maximumHeight);
            int width;
            int height;
            if (displayWidth * boxHeight >= displayHeight * boxWidth)
            {
                width = boxWidth;
                height = RoundWithin(width * displayHeight, displayWidth, boxHeight);
            }
            else
            {
                height = boxHeight;
                width = RoundWithin(height * displayWidth, displayHeight, boxWidth);
            }
            return NearestNeighborResize.Apply(input, width, height, SampleAspectRatio.Square, allocator, cancellationToken);
        }

        private static int RoundWithin(BigInteger numerator, BigInteger denominator, int maximum) =>
            (int)BigInteger.Clamp((numerator * 2 + denominator) / (denominator * 2), BigInteger.One, maximum);
    }
}
