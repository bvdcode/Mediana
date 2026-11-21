// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Processing
{
    /// <summary>Linearly reconstructs one stored field at full frame height. Existing rows are preserved;
    /// missing rows use the neighbouring rows of this field. No samples from the other field are used.</summary>
    public static class VideoFieldInterpolator
    {
        /// <summary>Borrows a single-field surface and returns an owned progressive frame. The full height is explicit
        /// because a field alone cannot distinguish an odd frame height from an even one. Timing is unchanged.</summary>
        public static VideoFrame Apply(VideoFrame field, int frameHeight, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(field);
            ArgumentNullException.ThrowIfNull(allocator);
            cancellationToken.ThrowIfCancellationRequested();
            (VideoFormat format, int offset) = Describe(field.Format, frameHeight);
            return Apply(field, format, offset, allocator, cancellationToken);
        }

        internal static (VideoFormat Format, int Offset) Describe(VideoFormat field, int frameHeight)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameHeight);
            VideoFieldParity parity = field.ScanMode switch
            {
                VideoScanMode.TopField => VideoFieldParity.Top,
                VideoScanMode.BottomField => VideoFieldParity.Bottom,
                VideoScanMode.Progressive or VideoScanMode.TopFieldFirst or VideoScanMode.BottomFieldFirst =>
                    throw new ArgumentException("Select one stored field before reconstructing its missing rows.", nameof(field)),
                _ => throw new ArgumentOutOfRangeException(nameof(field))
            };
            int offset = parity switch
            {
                VideoFieldParity.Top => 0,
                VideoFieldParity.Bottom => 1,
                _ => throw new ArgumentOutOfRangeException(nameof(field))
            };
            if (((long)frameHeight + 1 - offset) / 2 != field.Height)
            {
                throw new ArgumentException("The frame height does not match the stored field height and parity.", nameof(frameHeight));
            }
            if (field.FullFrameHeight is int declared && declared != frameHeight)
            {
                throw new ArgumentException("The frame height contradicts the field's declared full frame geometry.", nameof(frameHeight));
            }
            VideoPixelFormat pixels = field.PixelFormat;
            if (pixels is not PlanarVideoPixelFormat && pixels != VideoPixelFormat.Rgba32 && pixels != VideoPixelFormat.RgbaFloat32Premultiplied)
            {
                throw new NotSupportedException("Field reconstruction requires planar integer or RGBA pixels.");
            }
            VideoColorInfo color = field.Color;
            ChromaLocation location = color.FieldChromaLocations?.GetLocation(parity) ?? color.ChromaLocation;
            color = color with { ChromaLocation = location, FieldChromaLocations = null };
            return (new(field.Width, frameHeight, pixels, color, field.SampleAspectRatio, field.ColorDescription), offset);
        }

        internal static VideoFrame Apply(VideoFrame field, VideoFormat format, int offset, MediaBufferAllocator allocator, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            VideoPixelFormat pixels = format.PixelFormat;
            PlaneLayout[] layout = format.CreateTightLayout();
            using WritableMediaBuffer writable = allocator.Allocate(checked((int)layout[^1].EndOffset));
            for (int plane = 0; plane < layout.Length; plane++)
            {
                PlaneLayout destination = layout[plane];
                int last = field.Planes[plane].Height - 1;
                for (int row = 0; row < destination.Height; row++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int position = row - offset;
                    int upper = Math.Clamp(position / 2, 0, last);
                    ReadOnlySpan<byte> first = field.GetRow(plane, upper);
                    Span<byte> output = writable.Span.Slice(destination.Offset + row * destination.Stride, destination.RowBytes);
                    if (position <= 0 || upper == last || (position & 1) == 0)
                    {
                        first.CopyTo(output);
                    }
                    else
                    {
                        Average(first, field.GetRow(plane, upper + 1), output, pixels, plane, cancellationToken);
                    }
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            using MediaBuffer buffer = writable.Publish();
            VideoFramePresentation presentation = VideoFramePresentation.Frames(1, field.Presentation?.SourceScan ?? VideoSourceScan.Unknown);
            return new(format, buffer, layout, field.Timestamp, field.Duration, presentation);
        }

        private static void Average(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second, Span<byte> output,
            VideoPixelFormat pixels, int plane, CancellationToken token)
        {
            if (pixels == VideoPixelFormat.Rgba32)
            {
                AverageRgba(first, second, output, token);
            }
            else if (pixels == VideoPixelFormat.RgbaFloat32Premultiplied)
            {
                ReadOnlySpan<float> a = MemoryMarshal.Cast<byte, float>(first), b = MemoryMarshal.Cast<byte, float>(second);
                Span<float> destination = MemoryMarshal.Cast<byte, float>(output);
                for (int index = 0; index < destination.Length; index++)
                {
                    if ((index & 1023) == 0) { token.ThrowIfCancellationRequested(); }
                    destination[index] = a[index] * 0.5f + b[index] * 0.5f;
                }
            }
            else if (pixels is PlanarVideoPixelFormat planar && (plane == 3 ? planar.AlphaBitDepth : planar.BitDepth) > 8)
            {
                ReadOnlySpan<ushort> a = MemoryMarshal.Cast<byte, ushort>(first), b = MemoryMarshal.Cast<byte, ushort>(second);
                Span<ushort> destination = MemoryMarshal.Cast<byte, ushort>(output);
                VideoSampleAverage.Apply(a, b, destination, token);
            }
            else
            {
                VideoSampleAverage.Apply(first, second, output, token);
            }
        }

        private static void AverageRgba(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second, Span<byte> output, CancellationToken token)
        {
            for (int index = 0; index < output.Length; index += 4)
            {
                if ((index & 1023) == 0) { token.ThrowIfCancellationRequested(); }
                int a = first[index + 3], b = second[index + 3];
                int alpha = a + b;
                output[index + 3] = (byte)((alpha + 1) / 2);
                for (int channel = 0; channel < 3; channel++)
                {
                    output[index + channel] = alpha == 0 ? (byte)0 : (byte)((first[index + channel] * a + second[index + channel] * b + alpha / 2) / alpha);
                }
            }
        }
    }
}
