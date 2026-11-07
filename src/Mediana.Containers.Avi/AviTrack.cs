// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Containers.Avi
{
    internal class AviTrack(EncodedStreamInfo stream, uint sampleSize, uint start, uint length, AviPacketIndex packets) : IDisposable
    {
        public EncodedStreamInfo Stream { get; private set; } = stream;
        public TimeBase PresentationClock { get; set; } = stream.TimeBase;
        public void SetConfiguration(ReadOnlySpan<byte> configuration)
        {
            Stream = new(Stream.StreamIndex, Stream.Kind, Stream.CodecId, Stream.Format, Stream.TimeBase, configuration,
                Stream.DisplayMatrix, Stream.CodedDuration, Stream.ColorDescription, Stream.AspectRatioDescription);
        }
        public uint SampleSize { get; } = sampleSize;
        public long Start { get; } = start;
        public long DeclaredLength { get; } = length;
        public long Time { get; private set; } = start;
        public long PayloadBytes { get; private set; }
        public long Cursor { get; set; }
        public AviPacketIndex Packets { get; private set; } = packets;
        public AviChunk? Index { get; set; }

        public void Add(long offset, uint size, uint chunkId, bool keyFrame, bool noTime)
        {
            if (size > int.MaxValue) { throw new NotSupportedException("An AVI packet exceeds the supported memory length."); }
            if (SampleSize != 0 && size % SampleSize != 0)
            {
                throw new InvalidDataException("An AVI packet contains an incomplete fixed-size sample.");
            }
            long duration = 0;
            if (!noTime)
            {
                duration = SampleSize == 0 ? 1 : size / SampleSize;
                if (size == 0 && Stream.Kind == MediaKind.Video) { duration = 1; }
            }
            if (size != 0)
            {
                if (Packets.Count != 0 && offset - 8 < Packets[Packets.Count - 1].Offset + Packets[Packets.Count - 1].Length)
                {
                    throw new InvalidDataException("AVI packet index entries must not overlap or move backwards within a stream.");
                }
                if (Stream.CodecId == "mjpeg" && duration != 1)
                {
                    throw new NotSupportedException("A Motion JPEG AVI packet must contain one complete picture.");
                }
                Packets.Add(new(offset, Time, duration, (int)size, chunkId, keyFrame, offset, (int)size));
                PayloadBytes = checked(PayloadBytes + size);
            }
            else if (Stream.Kind == MediaKind.Video && Packets.Count != 0 && duration != 0)
            {
                Packets.ExtendLastDuration(duration);
            }
            Time = checked(Time + duration);
        }

        public void SetPackets(AviPacketIndex replacement)
        {
            long payload = 0;
            for (long index = 0; index < replacement.Count; index++) { payload = checked(payload + replacement[index].Length); }
            Packets.Dispose(); Packets = replacement; PayloadBytes = payload;
        }

        public void Dispose() => Packets.Dispose();
    }
}
