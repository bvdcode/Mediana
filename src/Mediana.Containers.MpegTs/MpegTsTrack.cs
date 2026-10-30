// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Formats;
using Mediana.Core.Memory;

using Mediana.Formats.Video;

using Mediana.Formats.Audio;

namespace Mediana.Containers.MpegTs
{
    internal class MpegTsTrack
    {
        private readonly AvcAnnexBConverter? avc;
        private readonly AacAdtsConverter? aac;
        private readonly Mp3TransportFraming? mp3;

        public MpegTsTrack(EncodedStreamInfo stream, int ordinal)
        {
            Stream = stream;
            StreamIndex = stream.StreamIndex;
            Pid = MpegTsTables.FirstMediaPid + ordinal;
            switch (stream.Kind, stream.CodecId)
            {
                case (MediaKind.Video, "h264"):
                    StreamFormats.Avc.Require(stream.Format);
                    Type = 0x1B;
                    PesStreamId = 0xE0 + ordinal;
                    avc = new(stream.Configuration);
                    break;
                case (MediaKind.Audio, "aac"):
                    StreamFormats.Aac.Require(stream.Format);
                    Type = 0x0F;
                    PesStreamId = 0xC0 + ordinal;
                    aac = new(stream.Configuration.Span);
                    break;
                case (MediaKind.Audio, "mp3"):
                    StreamFormats.Mp3.Require(stream.Format);
                    mp3 = new(stream.Configuration.Span);
                    Type = mp3.StreamType;
                    PesStreamId = 0xC0 + ordinal;
                    break;
                default:
                    throw new NotSupportedException("The TS writer supports length-prefixed AVC, raw AAC-LC and MPEG-1/2 Layer III streams.");
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
            if (mp3 is not null)
            {
                idr = false;
                return mp3.Convert(data, allocator);
            }
            throw new InvalidOperationException("The TS track has no elementary stream framing.");
        }
    }
}
