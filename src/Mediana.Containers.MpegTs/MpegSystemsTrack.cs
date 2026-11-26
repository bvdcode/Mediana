// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Metadata;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Formats;
using Mediana.Formats.Audio;

namespace Mediana.Containers.MpegTs
{
    internal class MpegSystemsTrack(int index, MediaKind kind, string codec, string packetFraming, string? language = null)
    {
        public static TimeBase Clock { get; } = new(1, 90_000);
        public EncodedStreamInfo Stream { get; private set; } = new(index, kind, codec, new("none", packetFraming, EncodedPacketBoundary.ArbitraryBytes), Clock);
        public StreamMetadata? Metadata { get; private set; }
        private FrameRate? nominalFrameRate;
        private long? reference;
        private long? firstPresentation;
        private long packetCount;
        private long payloadBytes;

        public MpegSystemsPacket? Add(long position, IReadOnlyList<MpegPayloadRange> ranges,
            MpegPesHeader header, bool? randomAccess, int skipBytes = 0)
        {
            int skip = header.HeaderLength + skipBytes;
            int remaining = header.PayloadLength is int size ? size - skipBytes : int.MaxValue;
            if (remaining < 0)
            {
                throw new InvalidDataException("The MPEG PES private header exceeds its payload.");
            }
            List<MpegPayloadRange> payload = new(ranges.Count);
            int length = 0;
            foreach (MpegPayloadRange range in ranges)
            {
                int discard = Math.Min(range.Length, skip);
                skip -= discard;
                int count = Math.Min(range.Length - discard, remaining);
                if (count == 0) { continue; }
                payload.Add(new(range.Offset + discard, count));
                length = checked(length + count);
                remaining -= count;
            }
            if (skip != 0 || (header.PayloadLength is not null && remaining != 0))
            {
                throw new InvalidDataException("The MPEG PES payload is truncated.");
            }
            if (length == 0) { return null; }
            long? decode = Expand(header.Dts, reference);
            long? presentation = Expand(header.Pts, decode ?? reference);
            reference = decode ?? presentation ?? reference;
            if (presentation is long time && (firstPresentation is null || time < firstPresentation.Value))
            {
                firstPresentation = time;
            }
            packetCount++;
            payloadBytes = checked(payloadBytes + length);
            return new(Stream.StreamIndex, position, payload.ToArray(), length,
                presentation is long pts ? new(pts, Clock) : null,
                decode is long dts ? new(dts, Clock) : null, randomAccess);
        }

        public void ReadOpeningMetadata(ReadOnlySpan<byte> opening)
        {
            if (Stream.CodecId == "h264")
            {
                SetVideoRepresentation(Stream.CodecId, StreamFormats.AvcAnnexB, ReadAvcSequence(opening));
            }
            else if (Stream.Format.PacketFraming == StreamFormats.AacAdtsByteStream.PacketFraming)
            {
                byte[] asc = opening.Length >= AdtsFrameHeader.ByteLength && AdtsFrameHeader.HasSync(opening)
                    ? AdtsFrameHeader.Read(opening).ToAudioSpecificConfig() : [];
                Stream = new(Stream.StreamIndex, Stream.Kind, Stream.CodecId, StreamFormats.AacAdtsByteStream, Clock, asc);
            }
            else if (Stream.CodecId is "ac3" or "eac3" or "dts")
            {
                ReadSyncframeMetadata(opening);
            }
            else if (Stream.CodecId == "mpeg-audio")
            {
                Metadata = ReadMpegAudioMetadata(opening);
            }
            else if (Stream.CodecId is "mpeg1video" or "mpeg2video" or "mpeg-video" &&
                MpegVideoSequence.Read(opening) is MpegVideoSequence video)
            {
                string id = video.Mpeg2 ? "mpeg2video" : "mpeg1video";
                EncodedStreamFormat format = video.Mpeg2 ? StreamFormats.Mpeg2Video : Stream.Format;
                SetVideoRepresentation(id, format, []);
                Metadata = video.Metadata;
                nominalFrameRate = video.Rate;
            }
        }

        private void SetVideoRepresentation(string codecId, EncodedStreamFormat format, ReadOnlySpan<byte> configuration)
        {
            Stream = new(Stream.StreamIndex, MediaKind.Video, codecId, format, Clock, configuration, VideoDisplayMatrix.Identity)
            {
                PresentationOrigin = firstPresentation is long time ? new(time, Clock) : null
            };
        }

        private void ReadSyncframeMetadata(ReadOnlySpan<byte> opening)
        {
            EncodedStreamFormat format = Stream.CodecId switch
            {
                "ac3" => StreamFormats.Ac3ByteStream,
                "eac3" => StreamFormats.Eac3ByteStream,
                "dts" => StreamFormats.DtsByteStream,
                _ => throw new InvalidOperationException("Unexpected MPEG syncframe codec.")
            };
            Stream = new(Stream.StreamIndex, Stream.Kind, Stream.CodecId, format, Clock);
            if (format == StreamFormats.DtsByteStream)
            {
                if (opening.Length >= DtsFrameHeader.MaximumHeaderBytes)
                {
                    DtsFrameHeader dts = DtsFrameHeader.Read(opening);
                    string[] channels = dts.GetChannelOrder();
                    Metadata = new AudioStreamMetadata(dts.SampleRate, channels.Length, channelOrder: channels)
                    {
                        CodecProfile = new("DTS Core")
                    };
                }
                return;
            }
            if (opening.Length >= Ac3FrameHeader.HeaderBytes && opening[0] == 0x0b && opening[1] == 0x77)
            {
                Ac3FrameHeader ac3 = Ac3FrameHeader.Read(opening);
                Metadata = new AudioStreamMetadata(ac3.SampleRate, ac3.ChannelCount, channelOrder: ac3.GetChannelOrder())
                {
                    CodecProfile = new(ac3.IsEnhanced ? "E-AC-3" : "AC-3")
                };
            }
        }

        private static AudioStreamMetadata? ReadMpegAudioMetadata(ReadOnlySpan<byte> opening)
        {
            if (opening.Length < 4 || opening[0] != 0xff || (opening[1] & 0xe0) != 0xe0)
            {
                return null;
            }
            int version = opening[1] >> 3 & 3;
            int layer = opening[1] >> 1 & 3;
            int index = opening[2] >> 2 & 3;
            if (version == 1 || index == 3 || layer == 0) { return null; }
            ReadOnlySpan<int> rates = [44100, 48000, 32000];
            int rate = version switch
            {
                0 => rates[index] / 4,
                2 => rates[index] / 2,
                3 => rates[index],
                _ => throw new InvalidOperationException("Unexpected MPEG audio version.")
            };
            string profile = layer switch
            {
                1 => "Layer III",
                2 => "Layer II",
                3 => "Layer I",
                _ => throw new InvalidOperationException("Unexpected MPEG audio layer.")
            };
            bool mono = (opening[3] >> 6) == 3;
            return new(rate, mono ? 1 : 2, channelOrder: mono ? ["front-center"] : ["front-left", "front-right"])
            {
                CodecProfile = new(profile)
            };
        }

        private static ReadOnlySpan<byte> ReadAvcSequence(ReadOnlySpan<byte> data)
        {
            ReadOnlySpan<byte> startCode = [0, 0, 1];
            int index = data.IndexOf(startCode);
            while (index >= 0 && data.Length > index + startCode.Length)
            {
                ReadOnlySpan<byte> unit = data[(index + startCode.Length)..];
                int next = unit.IndexOf(startCode);
                if (next < 0) { break; }
                if ((unit[0] & 31) == 7)
                {
                    ReadOnlySpan<byte> sequence = unit[..next].TrimEnd((byte)0);
                    return sequence;
                }
                data = unit;
                index = next;
            }
            return [];
        }

        public ContainerStreamInfo Describe() => new(Stream.StreamIndex, null, null, new(packetCount, payloadBytes),
            language, null, null, nominalFrameRate);

        private static long? Expand(long? raw, long? previous)
        {
            if (raw is not long value || previous is not long anchor) { return raw; }
            const long wrap = 1L << 33;
            long candidate = (anchor & ~(wrap - 1)) | value;
            long delta = candidate - anchor;
            if (delta >= wrap / 2) { return candidate - wrap; }
            if (delta < -wrap / 2) { return candidate + wrap; }
            return candidate;
        }
    }
}
