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
            : this(format, buffer, planes, timestamp, duration, null)
        {
        }

        public VideoFrame(VideoFormat format, MediaBuffer buffer, IReadOnlyList<PlaneLayout> planes,
            MediaTimestamp? timestamp, MediaDuration? duration, VideoFramePresentation? presentation)
        {
            ArgumentNullException.ThrowIfNull(format);
            ArgumentNullException.ThrowIfNull(buffer);
            ArgumentNullException.ThrowIfNull(planes);
            timestamp?.TimeBase.Validate();
            duration?.TimeBase.Validate();
            ValidatePresentation(format, presentation);
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
            Presentation = presentation;
            this.buffer = buffer.Retain();
        }

        public VideoFormat Format { get; }
        public IReadOnlyList<PlaneLayout> Planes { get; }
        public MediaTimestamp? Timestamp { get; }
        public MediaDuration? Duration { get; }
        public VideoFramePresentation? Presentation { get; }
        public ReadOnlyMemory<byte> Data => buffer.Memory;

        public ReadOnlySpan<byte> GetRow(int planeIndex, int row)
        {
            PlaneLayout plane = Planes[planeIndex];
            ArgumentOutOfRangeException.ThrowIfNegative(row);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, plane.Height);
            return Data.Span.Slice(checked(plane.Offset + row * plane.Stride), plane.RowBytes);
        }

        public VideoFrame Retain() => new(Format, buffer, Planes, Timestamp, Duration, Presentation);
        /// <summary>Returns an independent lease over the same pixels with explicitly replaced timing. Does not copy pixel storage.</summary>
        public VideoFrame WithTiming(MediaTimestamp? timestamp, MediaDuration? duration) => new(Format, buffer, Planes, timestamp, duration, Presentation);
        /// <summary>Returns an independent lease with an explicitly replaced format and unchanged timing/pixels.
        /// The replacement must describe the existing plane geometry; no sample conversion is performed.</summary>
        public VideoFrame WithFormat(VideoFormat format) => new(format, buffer, Planes, Timestamp, Duration, Presentation);

        /// <summary>Returns an independent lease with replaced display hints and unchanged pixels and timing.</summary>
        public VideoFrame WithPresentation(VideoFramePresentation? presentation) => new(Format, buffer, Planes, Timestamp, Duration, presentation);

        /// <summary>Returns an independent, strided lease over one stored field without copying pixels.
        /// Timing remains that of the source surface; no field duration or timestamp is inferred.</summary>
        public VideoFrame GetField(VideoFieldParity parity)
        {
            VideoFormat format = Format.GetFieldFormat(parity);
            if (Format.ScanMode is VideoScanMode.TopField or VideoScanMode.BottomField)
            {
                return Retain();
            }
            int offset = parity switch
            {
                VideoFieldParity.Top => 0,
                VideoFieldParity.Bottom => 1,
                _ => throw new ArgumentOutOfRangeException(nameof(parity))
            };
            PlaneLayout[] layout = format.CreateTightLayout();
            for (int plane = 0; plane < layout.Length; plane++)
            {
                PlaneLayout source = Planes[plane];
                int stride = layout[plane].Height == 1 ? source.Stride : checked(source.Stride * 2);
                layout[plane] = new(checked(source.Offset + offset * source.Stride), stride, source.RowBytes, layout[plane].Height);
            }
            VideoFramePresentation? presentation = Presentation is VideoFramePresentation sourcePresentation
                ? VideoFramePresentation.Fields(parity, 1, sourcePresentation.SourceScan) : null;
            return new(format, buffer, layout, Timestamp, Duration, presentation);
        }

        public void Dispose() => buffer.Dispose();

        private static void ValidatePresentation(VideoFormat format, VideoFramePresentation? presentation)
        {
            if (presentation is not VideoFramePresentation value)
            {
                return;
            }
            value.Validate();
            VideoFieldParity? field = format.ScanMode switch
            {
                VideoScanMode.Progressive or VideoScanMode.TopFieldFirst or VideoScanMode.BottomFieldFirst => null,
                VideoScanMode.TopField => VideoFieldParity.Top,
                VideoScanMode.BottomField => VideoFieldParity.Bottom,
                _ => throw new ArgumentOutOfRangeException(nameof(format))
            };
            if (field is not null && (value.Unit != VideoPresentationUnit.Field || value.FirstField != field || value.DisplayCount != 1))
            {
                throw new ArgumentException("A single-field surface can only present its stored field.", nameof(presentation));
            }
        }
    }
}
