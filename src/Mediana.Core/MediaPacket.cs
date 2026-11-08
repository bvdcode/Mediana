// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Core
{
    /// <summary>An encoded media packet with its own lease. Packet boundaries need not equal frame boundaries.</summary>
    public class MediaPacket : IDisposable
    {
        private readonly MediaBuffer buffer;
        private readonly MediaPacketSideData[]? sideData;

        public MediaPacket(int streamIndex, MediaBuffer buffer, MediaTimestamp? presentationTime = null,
            MediaTimestamp? decodeTime = null, MediaDuration? duration = null, bool? isKeyFrame = null, IReadOnlyList<MediaPacketSideData>? sideData = null)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(streamIndex);
            ArgumentNullException.ThrowIfNull(buffer);
            presentationTime?.TimeBase.Validate();
            decodeTime?.TimeBase.Validate();
            duration?.TimeBase.Validate();
            StreamIndex = streamIndex;
            PresentationTime = presentationTime;
            DecodeTime = decodeTime;
            Duration = duration;
            IsKeyFrame = isKeyFrame;
            MediaBuffer payload = buffer.Retain();
            bool succeeded = false;
            try
            {
                this.sideData = RetainSideData(sideData);
                this.buffer = payload;
                succeeded = true;
            }
            finally
            {
                if (!succeeded)
                {
                    payload.Dispose();
                }
            }
        }

        public int StreamIndex { get; }
        public MediaTimestamp? PresentationTime { get; }
        public MediaTimestamp? DecodeTime { get; }
        public MediaDuration? Duration { get; }
        /// <summary>Container sync indication, when known. This alone does not prove independent segment decoding.</summary>
        public bool? IsKeyFrame { get; }
        public ReadOnlyMemory<byte> Data => buffer.Memory;
        /// <summary>Returns an independently owned lease of the immutable payload without copying.
        /// It survives packet disposal and retains the original backing allocation and its budget.</summary>
        public MediaBuffer RetainData() => buffer.Retain();
        public int SideDataCount => sideData?.Length ?? 0;

        public MediaPacketSideDataView GetSideData(int index)
        {
            MediaPacketSideData data = GetSideDataEntry(index);
            return new(data.Kind, data.Data);
        }

        /// <summary>Returns an independently owned side-data lease without copying its backing memory.</summary>
        public MediaPacketSideData RetainSideData(int index) => GetSideDataEntry(index).Retain();

        public bool TryGetSideData(string kind, out MediaPacketSideDataView value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(kind);
            if (sideData is not null)
            {
                foreach (MediaPacketSideData data in sideData)
                {
                    if (StringComparer.Ordinal.Equals(data.Kind, kind))
                    {
                        value = new(data.Kind, data.Data);
                        return true;
                    }
                }
            }
            value = default;
            return false;
        }
        public MediaPacket Retain() => new(StreamIndex, buffer, PresentationTime, DecodeTime, Duration, IsKeyFrame, sideData);
        /// <summary>Retains the same immutable payload and timestamps with an explicit duration, including unknown.
        /// The original packet remains independently owned and unchanged.</summary>
        public MediaPacket RetainWithDuration(MediaDuration? duration) =>
            new(StreamIndex, buffer, PresentationTime, DecodeTime, duration, IsKeyFrame, sideData);
        public void Dispose()
        {
            buffer.Dispose();
            DisposeSideData(sideData);
        }

        private MediaPacketSideData GetSideDataEntry(int index)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, SideDataCount);
            return sideData![index];
        }

        private static MediaPacketSideData[]? RetainSideData(IReadOnlyList<MediaPacketSideData>? source)
        {
            int count = source?.Count ?? 0;
            if (count == 0)
            {
                return null;
            }
            MediaPacketSideData[] retained = new MediaPacketSideData[count];
            bool succeeded = false;
            try
            {
                for (int index = 0; index < count; index++)
                {
                    MediaPacketSideData current = source![index];
                    ArgumentNullException.ThrowIfNull(current);
                    for (int previous = 0; previous < index; previous++)
                    {
                        if (StringComparer.Ordinal.Equals(retained[previous].Kind, current.Kind))
                        {
                            throw new ArgumentException("Packet side-data kinds must be unique.", nameof(source));
                        }
                    }
                    retained[index] = current.Retain();
                }
                succeeded = true;
                return retained;
            }
            finally
            {
                if (!succeeded)
                {
                    DisposeSideData(retained);
                }
            }
        }

        private static void DisposeSideData(MediaPacketSideData[]? values)
        {
            if (values is not null)
            {
                foreach (MediaPacketSideData? value in values)
                {
                    value?.Dispose();
                }
            }
        }
    }
}
