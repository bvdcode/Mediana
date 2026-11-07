// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.InteropServices;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Processing
{
    public static partial class SrgbConverter
    {
        private static readonly VideoColorInfo SrgbColor = new(ColorPrimaries.Bt709, TransferFunction.Srgb,
            MatrixCoefficients.Rgb, ColorRange.Full, ChromaLocation.Unspecified);

        /// <summary>Converts SDR planar integer YUV/GBR or straight RGBA32 to full-range straight sRGB RGBA32.
        /// Uses supported ICC v2/v4 matrix/TRC profiles or known source color parameters.
        /// Borrows input; returns an independently disposable frame.
        /// Unchanged tightly packed sRGB storage retains its original allocation budget without copying.
        /// Clips nominal source RGB before display transfer and target RGB to the sRGB gamut.
        /// BT.709 and SMPTE 240M use an ideal BT.1886 reference display. Does not perform HDR tone mapping.</summary>
        public static VideoFrame Apply(VideoFrame input, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default)
        {
            return ApplyWithColorPolicy(input, null, allocator, cancellationToken);
        }

        /// <summary>Uses the explicitly supplied source color interpretation in place of the frame metadata.</summary>
        public static VideoFrame Apply(VideoFrame input, VideoColorInfo sourceColor, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            NearestNeighborMap map = new(input.Format.Width, input.Format.Height, input.Format.Width, input.Format.Height,
                OrthogonalTransform.Identity);
            return Apply(input, sourceColor, map, input.Format.SampleAspectRatio, allocator, cancellationToken);
        }

        internal static VideoFrame Apply(VideoFrame input, VideoColorInfo sourceColor, NearestNeighborMap map,
            SampleAspectRatio? aspectRatio, MediaBufferAllocator allocator, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(sourceColor);
            ArgumentNullException.ThrowIfNull(allocator);
            cancellationToken.ThrowIfCancellationRequested();
            SdrRgbTransform transform = new(sourceColor.Primaries, sourceColor.Transfer);
            PlanarVideoPixelFormat? planar = input.Format.PixelFormat as PlanarVideoPixelFormat;
            bool yuv = planar?.ColorModel == PlanarColorModel.Yuv;
            bool gbr = planar?.ColorModel == PlanarColorModel.Gbr;
            YuvRgbMatrix matrix = default;
            if (yuv)
            {
                matrix = YuvRgbMatrix.Create(sourceColor.Matrix, sourceColor.Range, planar!.BitDepth);
            }
            else if ((!gbr && !ReferenceEquals(input.Format.PixelFormat, VideoPixelFormat.Rgba32)) || sourceColor.Matrix != MatrixCoefficients.Rgb ||
                sourceColor.Range != ColorRange.Full || sourceColor.ChromaLocation != ChromaLocation.Unspecified)
            {
                throw new NotSupportedException("RGB conversion requires full-range straight RGBA32 with an RGB matrix and no chroma subsampling.");
            }
            if (!yuv && !gbr && CanRetain(input, sourceColor, map, aspectRatio))
            {
                return input.Retain();
            }
            if (!yuv && !gbr)
            {
                return ApplyRgba(input, map, aspectRatio, allocator, transform, cancellationToken);
            }
            VideoFormat format = new(map.OutputWidth, map.OutputHeight, VideoPixelFormat.Rgba32, SrgbColor, aspectRatio);
            PlaneLayout[] planes = format.CreateTightLayout();
            using WritableMediaBuffer writable = allocator.Allocate(checked((int)planes[0].EndOffset));
            if (gbr)
            {
                if (planar!.BitDepth > 8) { RenderGbr<ushort>(input, writable.Span, map, planar.BitDepth, transform, cancellationToken); }
                else { RenderGbr<byte>(input, writable.Span, map, planar.BitDepth, transform, cancellationToken); }
            }
            else if (planar!.BitDepth > 8) { RenderYuv<ushort>(input, writable.Memory.Span, map, matrix, sourceColor.ChromaLocation, transform, cancellationToken); }
            else { RenderYuv<byte>(input, writable.Memory.Span, map, matrix, sourceColor.ChromaLocation, transform, cancellationToken); }
            cancellationToken.ThrowIfCancellationRequested();
            using MediaBuffer buffer = writable.Publish();
            return new VideoFrame(format, buffer, planes, input.Timestamp, input.Duration);
        }

        private static bool CanRetain(VideoFrame input, VideoColorInfo sourceColor, NearestNeighborMap map,
            SampleAspectRatio? aspectRatio)
        {
            PlaneLayout plane = input.Planes[0];
            int rowBytes = checked(input.Format.Width * 4);
            return sourceColor == SrgbColor && input.Format.Color == SrgbColor && input.Format.ColorDescription is null
                && map.IsIdentity && input.Format.SampleAspectRatio == aspectRatio
                && plane.Offset == 0 && plane.Stride == rowBytes && input.Data.Length == (long)rowBytes * input.Format.Height;
        }

        private static void RenderYuv<TSample>(VideoFrame input, Span<byte> output, NearestNeighborMap map, YuvRgbMatrix matrix,
            ChromaLocation location, SdrRgbTransform transform, CancellationToken cancellationToken) where TSample : unmanaged, IBinaryInteger<TSample>
        {
            int cachedRow = -1;
            ReadOnlySpan<TSample> luma = default;
            BilinearChromaRow<TSample> cb = default;
            BilinearChromaRow<TSample> cr = default;
            for (int y = 0; y < map.OutputHeight; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Span<byte> row = output.Slice(y * map.OutputWidth * 4, map.OutputWidth * 4);
                for (int x = 0; x < map.OutputWidth; x++)
                {
                    if ((x & 255) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    int sourceX = map.SourceX(x, y);
                    int sourceY = map.SourceY(x, y);
                    if (sourceY != cachedRow)
                    {
                        luma = MemoryMarshal.Cast<byte, TSample>(input.GetRow(0, sourceY));
                        cb = new(input, 1, sourceY, location);
                        cr = new(input, 2, sourceY, location);
                        cachedRow = sourceY;
                    }
                    (double red, double green, double blue) = matrix.Convert(double.CreateChecked(luma[sourceX]), cb.Sample(sourceX), cr.Sample(sourceX));
                    transform.Write(row.Slice(x * 4, 4), red, green, blue, 255);
                }
            }
        }

        private static void RenderRgba<TTransform>(VideoFrame input, Span<byte> output, NearestNeighborMap map, TTransform transform,
            CancellationToken cancellationToken) where TTransform : IRgbaPixelTransform
        {
            int cachedRow = -1;
            ReadOnlySpan<byte> source = default;
            for (int y = 0; y < map.OutputHeight; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Span<byte> row = output.Slice(y * map.OutputWidth * 4, map.OutputWidth * 4);
                for (int x = 0; x < map.OutputWidth; x++)
                {
                    if ((x & 255) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    int sourceY = map.SourceY(x, y);
                    if (sourceY != cachedRow)
                    {
                        source = input.GetRow(0, sourceY);
                        cachedRow = sourceY;
                    }
                    ReadOnlySpan<byte> pixel = source.Slice(map.SourceX(x, y) * 4, 4);
                    transform.WritePixel(row.Slice(x * 4, 4), pixel);
                }
            }
        }
    }
}
