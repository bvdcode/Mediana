// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core;
using Mediana.Core.Metadata;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Formats;
using Mediana.Formats.Video;

namespace Mediana.Containers.Asf
{
    internal class AsfStream(int number, EncodedStreamInfo stream, StreamMetadata? metadata)
    {
        public static TimeBase Clock { get; } = new(1, 1000);
        public int Number { get; } = number;
        public EncodedStreamInfo Stream { get; } = stream;
        public StreamMetadata? Metadata { get; } = metadata;
        public long? DeclaredBitRate { get; init; }
        public long Bytes { get; set; }
        public long Count { get; set; }

        public static AsfStream? Read(ReadOnlySpan<byte> data, int index)
        {
            if (data.Length < 54) { throw new InvalidDataException("Truncated ASF Stream Properties object."); }
            Guid type = new(data[..16]);
            uint typeLength = BinaryPrimitives.ReadUInt32LittleEndian(data[40..]);
            uint errorLength = BinaryPrimitives.ReadUInt32LittleEndian(data[44..]);
            if ((ulong)typeLength + errorLength > (ulong)(data.Length - 54))
            {
                throw new InvalidDataException("ASF stream description exceeds its object.");
            }
            int flags = BinaryPrimitives.ReadUInt16LittleEndian(data[48..]);
            int number = flags & 0x7f;
            if (number == 0) { throw new InvalidDataException("Invalid ASF stream number."); }
            if ((flags & 0x8000) != 0) { throw new NotSupportedException("Encrypted ASF streams are not supported."); }
            if (type == AsfObjects.Video) { return ReadVideo(number, index, data.Slice(54, (int)typeLength)); }
            if (type == AsfObjects.Audio) { return ReadAudio(number, index, data.Slice(54, (int)typeLength)); }
            return null;
        }

        private static AsfStream ReadVideo(int number, int index, ReadOnlySpan<byte> data)
        {
            if (data.Length < 51) { throw new InvalidDataException("Truncated ASF video type data."); }
            int size = BinaryPrimitives.ReadUInt16LittleEndian(data[9..]);
            ReadOnlySpan<byte> bitmap = data[11..];
            int width = BinaryPrimitives.ReadInt32LittleEndian(bitmap[4..]);
            int height = BinaryPrimitives.ReadInt32LittleEndian(bitmap[8..]);
            if (size < 40 || size > bitmap.Length || BinaryPrimitives.ReadUInt32LittleEndian(bitmap) < 40 ||
                width <= 0 || height <= 0)
            {
                throw new InvalidDataException("Invalid ASF video bitmap description.");
            }
            uint tag = BinaryPrimitives.ReadUInt32LittleEndian(bitmap[16..]);
            (string codec, EncodedStreamFormat format) = tag switch
            {
                0x32564d57 => (MicrosoftMpeg4Configuration.Wmv2CodecId, StreamFormats.Wmv2),
                0x3334504d => (MicrosoftMpeg4Configuration.Version3CodecId, StreamFormats.MicrosoftMpeg4V3),
                0x31564d57 => ("wmv1", new("bitmap.codec-private", "wmv1.picture", EncodedPacketBoundary.AccessUnit)),
                0x33564d57 => ("wmv3", new("bitmap.codec-private", "wmv3.picture", EncodedPacketBoundary.AccessUnit)),
                0x31435657 => ("vc1", new("bitmap.codec-private", "vc1.picture", EncodedPacketBoundary.AccessUnit)),
                _ => ($"asf-video:{tag:x8}", new("bitmap.codec-private", "asf.media-object", EncodedPacketBoundary.AccessUnit))
            };
            byte[] extra = bitmap[40..size].ToArray();
            StreamMetadata? metadata = null;
            if (codec is MicrosoftMpeg4Configuration.Wmv2CodecId or MicrosoftMpeg4Configuration.Version3CodecId)
            {
                extra = new MicrosoftMpeg4Configuration(width, height, extra).Write();
            }
            if (codec is MicrosoftMpeg4Configuration.Wmv2CodecId or MicrosoftMpeg4Configuration.Version3CodecId or "wmv1" or "wmv3" or "vc1")
            {
                metadata = new VideoStreamMetadata([new(width, height, VideoChromaSampling.Sampling420, 8, 8)
                {
                    PixelFormat = VideoPixelFormat.Yuv420P8
                }]);
            }
            return new(number, new(index, MediaKind.Video, codec, format, Clock, extra, VideoDisplayMatrix.Identity), metadata);
        }

        private static AsfStream ReadAudio(int number, int index, ReadOnlySpan<byte> data)
        {
            if (data.Length < 16) { throw new InvalidDataException("Truncated ASF audio type data."); }
            int tag = BinaryPrimitives.ReadUInt16LittleEndian(data);
            int channels = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
            uint rate = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
            if (channels == 0 || rate is 0 or > int.MaxValue) { throw new InvalidDataException("Invalid ASF audio parameters."); }
            string codec = tag switch
            {
                0x0050 => "mpeg-audio",
                0x0055 => "mp3",
                0x0160 => "wmav1",
                0x0161 => "wmav2",
                0x0162 => "wmapro",
                0x0163 => "wmalossless",
                0x0164 => "wmavoice",
                _ => $"asf-audio:{tag:x4}"
            };
            uint bytesPerSecond = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
            IReadOnlyList<string>? channelOrder = channels switch
            {
                1 => ["front-center"],
                2 => ["front-left", "front-right"],
                _ => null
            };
            return new(number, new(index, MediaKind.Audio, codec, StreamFormats.WaveFormatMediaObjects, Clock, data),
                new AudioStreamMetadata((int)rate, channels, channelOrder: channelOrder))
            {
                DeclaredBitRate = bytesPerSecond == 0 ? null : (long)bytesPerSecond * 8
            };
        }
    }
}
