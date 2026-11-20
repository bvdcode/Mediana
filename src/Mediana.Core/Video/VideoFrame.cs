// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Core.Video
{
    /// <summary>Retains its own buffer lease. Dispose each frame independently.</summary>
    public class VideoFrame : IDisposable
    {
        private readonly MediaBuffer buffer;

        public VideoFrame(VideoFormat format, MediaBuffer buffer, IReadOnlyList<PlaneLayout> planes,
            MediaTimestamp? timestamp = null, MediaDuration? duration = null)
        {
            ArgumentNullException.ThrowIfNull(format);
            ArgumentNullException.ThrowIfNull(buffer);
            ArgumentNullException.ThrowIfNull(planes);
            timestamp?.TimeBase.Validate();
            duration?.TimeBase.Validate();
            PlaneLayout[] layout = planes.ToArray();
            PlaneLayout[] expected = format.CreateTightLayout();
            if (layout.Length != expected.Length)
            {
                throw new ArgumentException("Plane count does not match the pixel format.", nameof(planes));
            }
            for (int i = 0; i < layout.Length; i++)
            {
                layout[i].Validate(buffer.Memory.Length);
                if (layout[i].RowBytes != expected[i].RowBytes || layout[i].Height != expected[i].Height)
                {
                    throw new ArgumentException("Plane dimensions do not match the pixel format.", nameof(planes));
                }
            }
            Format = format;
            Planes = Array.AsReadOnly(layout);
            Timestamp = timestamp;
            Duration = duration;
            this.buffer = buffer.Retain();
        }

        public VideoFormat Format { get; }
        public IReadOnlyList<PlaneLayout> Planes { get; }
        public MediaTimestamp? Timestamp { get; }
        public MediaDuration? Duration { get; }
        public ReadOnlyMemory<byte> Data => buffer.Memory;

        public ReadOnlySpan<byte> GetRow(int planeIndex, int row)
        {
            PlaneLayout plane = Planes[planeIndex];
            ArgumentOutOfRangeException.ThrowIfNegative(row);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, plane.Height);
            return Data.Span.Slice(checked(plane.Offset + row * plane.Stride), plane.RowBytes);
        }

        public VideoFrame Retain() => new(Format, buffer, Planes, Timestamp, Duration);
        /// <summary>Returns an independent lease over the same pixels with explicitly replaced timing. Does not copy pixel storage.</summary>
        public VideoFrame WithTiming(MediaTimestamp? timestamp, MediaDuration? duration) => new(Format, buffer, Planes, timestamp, duration);
        /// <summary>Returns an independent lease with an explicitly replaced format and unchanged timing/pixels.
        /// The replacement must describe the existing plane geometry; no sample conversion is performed.</summary>
        public VideoFrame WithFormat(VideoFormat format) => new(format, buffer, Planes, Timestamp, Duration);

        /// <summary>Returns an independent, strided lease over one stored field without copying pixels.
        /// Timing remains that of the source surface; no field duration or timestamp is inferred.</summary>
        public VideoFrame GetField(VideoFieldParity parity)
        {
            int offset = parity switch
            {
                VideoFieldParity.Top => 0,
                VideoFieldParity.Bottom => 1,
                _ => throw new ArgumentOutOfRangeException(nameof(parity))
            };
            switch (Format.ScanMode)
            {
                case VideoScanMode.TopField when parity == VideoFieldParity.Top:
                case VideoScanMode.BottomField when parity == VideoFieldParity.Bottom:
                    return Retain();
                case VideoScanMode.TopField:
                case VideoScanMode.BottomField:
                    throw new ArgumentException("The requested field is not present in this surface.", nameof(parity));
                case VideoScanMode.Progressive:
                    throw new InvalidOperationException("Progressive chroma cannot be interpreted as independent fields without resampling.");
                case VideoScanMode.TopFieldFirst:
                case VideoScanMode.BottomFieldFirst:
                    break;
                default:
                    throw new InvalidOperationException("The video scan mode is invalid.");
            }
            int height = checked((int)(((long)Format.Height + 1 - offset) / 2));
            if (height == 0)
            {
                throw new ArgumentException("The requested field has no visible rows.", nameof(parity));
            }
            VideoScanMode mode = parity switch
            {
                VideoFieldParity.Top => VideoScanMode.TopField,
                VideoFieldParity.Bottom => VideoScanMode.BottomField,
                _ => throw new ArgumentOutOfRangeException(nameof(parity))
            };
            VideoFormat format = new(Format.Width, height, Format.PixelFormat, Format.Color, Format.SampleAspectRatio,
                Format.ColorDescription, mode);
            PlaneLayout[] layout = format.CreateTightLayout();
            for (int plane = 0; plane < layout.Length; plane++)
            {
                PlaneLayout source = Planes[plane];
                int stride = layout[plane].Height == 1 ? source.Stride : checked(source.Stride * 2);
                layout[plane] = new(checked(source.Offset + offset * source.Stride), stride, source.RowBytes, layout[plane].Height);
            }
            return new(format, buffer, layout, Timestamp, Duration);
        }

        public void Dispose() => buffer.Dispose();
    }
}
