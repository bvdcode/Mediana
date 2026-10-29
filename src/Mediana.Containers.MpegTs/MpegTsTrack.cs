// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;

namespace Mediana.Containers.MpegTs
{
    internal class MpegTsTrack
    {
        private readonly AvcTransportFraming? avc;
        private readonly AacTransportFraming? aac;

        public MpegTsTrack(EncodedStreamInfo stream, int ordinal)
        {
            Stream = stream;
            StreamIndex = stream.StreamIndex;
            Pid = MpegTsTables.FirstMediaPid + ordinal;
            switch (stream.Kind, stream.CodecId)
            {
                case (MediaKind.Video, "h264"):
                    Type = 0x1B;
                    PesStreamId = 0xE0 + ordinal;
                    avc = new(stream.Configuration);
                    break;
                case (MediaKind.Audio, "aac"):
                    Type = 0x0F;
                    PesStreamId = 0xC0 + ordinal;
                    aac = new(stream.Configuration.Span);
                    break;
                default:
                    throw new NotSupportedException("The TS writer supports length-prefixed AVC and raw AAC-LC streams.");
            }
        }

        public int StreamIndex { get; }
        public EncodedStreamInfo Stream { get; }
        public int Pid { get; }
        public byte Type { get; }
        public int PesStreamId { get; }
        public int Counter { get; set; } = 15;
        public long? LastDecodeTime { get; set; }

        public MediaBuffer Frame(ReadOnlySpan<byte> data, MediaBufferAllocator allocator, int maximumBytes, out bool idr)
        {
            if (avc is not null)
            {
                return avc.Convert(data, allocator, maximumBytes, out idr);
            }
            if (aac is not null)
            {
                idr = false;
                if (data.Length > maximumBytes - 7)
                {
                    throw new NotSupportedException("The ADTS frame exceeds the configured packet limit.");
                }
                return aac.Convert(data, allocator);
            }
            throw new InvalidOperationException("The TS track has no elementary stream framing.");
        }
    }
}
