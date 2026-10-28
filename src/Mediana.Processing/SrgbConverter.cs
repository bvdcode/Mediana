using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Processing
{
    public static class SrgbConverter
    {
        private static readonly VideoColorInfo SrgbColor = new(ColorPrimaries.Bt709, TransferFunction.Srgb,
            MatrixCoefficients.Rgb, ColorRange.Full, ChromaLocation.Unspecified);

        /// <summary>Converts supported SDR YUV420P8 or straight RGBA32 to full-range straight sRGB RGBA32.
        /// Requires known source color parameters. Borrows input; returns an independent owned frame.
        /// Clips nominal source RGB before display transfer and target RGB to the sRGB gamut.
        /// BT.709 and SMPTE 240M use an ideal BT.1886 reference display. Does not perform HDR tone mapping.</summary>
        public static VideoFrame Apply(VideoFrame input, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            return Apply(input, input.Format.Color, allocator, cancellationToken);
        }

        /// <summary>Uses the explicitly supplied source color interpretation in place of the frame metadata.</summary>
        public static VideoFrame Apply(VideoFrame input, VideoColorInfo sourceColor, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(sourceColor);
            ArgumentNullException.ThrowIfNull(allocator);
            cancellationToken.ThrowIfCancellationRequested();
            SdrRgbTransform transform = new(sourceColor.Primaries, sourceColor.Transfer);
            bool yuv = ReferenceEquals(input.Format.PixelFormat, VideoPixelFormat.Yuv420P8);
            YuvRgbMatrix matrix = default;
            if (yuv)
            {
                matrix = YuvRgbMatrix.Create(sourceColor.Matrix, sourceColor.Range);
            }
            else if (!ReferenceEquals(input.Format.PixelFormat, VideoPixelFormat.Rgba32) || sourceColor.Matrix != MatrixCoefficients.Rgb ||
                sourceColor.Range != ColorRange.Full || sourceColor.ChromaLocation != ChromaLocation.Unspecified)
            {
                throw new NotSupportedException("RGB conversion requires full-range straight RGBA32 with an RGB matrix and no chroma subsampling.");
            }
            VideoFormat format = new(input.Format.Width, input.Format.Height, VideoPixelFormat.Rgba32,
                SrgbColor, input.Format.SampleAspectRatio);
            PlaneLayout[] planes = format.CreateTightLayout();
            using WritableMediaBuffer writable = allocator.Allocate(checked((int)planes[0].EndOffset));
            if (yuv)
            {
                RenderYuv(input, writable.Memory.Span, planes[0].Stride, matrix, sourceColor.ChromaLocation, transform, cancellationToken);
            }
            else
            {
                RenderRgba(input, writable.Memory.Span, planes[0].Stride, transform, cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            using MediaBuffer buffer = writable.Publish();
            return new VideoFrame(format, buffer, planes, input.Timestamp, input.Duration);
        }

        private static void RenderYuv(VideoFrame input, Span<byte> output, int stride, YuvRgbMatrix matrix,
            ChromaLocation location, SdrRgbTransform transform, CancellationToken cancellationToken)
        {
            for (int y = 0; y < input.Format.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReadOnlySpan<byte> luma = input.GetRow(0, y);
                BilinearChromaRow cb = new(input, 1, y, location);
                BilinearChromaRow cr = new(input, 2, y, location);
                Span<byte> row = output.Slice(y * stride, stride);
                for (int x = 0; x < input.Format.Width; x++)
                {
                    if ((x & 255) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    (double red, double green, double blue) = matrix.Convert(luma[x], cb.Sample(x), cr.Sample(x));
                    transform.Write(row.Slice(x * 4, 4), red, green, blue, 255);
                }
            }
        }

        private static void RenderRgba(VideoFrame input, Span<byte> output, int stride, SdrRgbTransform transform,
            CancellationToken cancellationToken)
        {
            for (int y = 0; y < input.Format.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReadOnlySpan<byte> source = input.GetRow(0, y);
                Span<byte> row = output.Slice(y * stride, stride);
                for (int x = 0; x < input.Format.Width; x++)
                {
                    if ((x & 255) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    ReadOnlySpan<byte> pixel = source.Slice(x * 4, 4);
                    transform.Write(row.Slice(x * 4, 4), pixel[0] / 255.0, pixel[1] / 255.0, pixel[2] / 255.0, pixel[3]);
                }
            }
        }
    }
}
