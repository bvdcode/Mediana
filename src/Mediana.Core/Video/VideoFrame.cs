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
        public void Dispose() => buffer.Dispose();
    }
}
