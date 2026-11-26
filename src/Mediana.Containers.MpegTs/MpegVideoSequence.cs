// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core.Metadata;
using Mediana.Core.Video;

namespace Mediana.Containers.MpegTs
{
    internal readonly record struct MpegVideoSequence(bool Mpeg2, FrameRate Rate, VideoStreamMetadata Metadata)
    {
        public static MpegVideoSequence? Read(ReadOnlySpan<byte> data)
        {
            int position = data.IndexOf(new byte[] { 0, 0, 1, 0xb3 });
            if (position < 0 || data.Length - position < 12) { return null; }
            ReadOnlySpan<byte> header = data[(position + 4)..];
            int width = header[0] << 4 | header[1] >> 4;
            int height = (header[1] & 15) << 8 | header[2];
            int aspect = header[3] >> 4;
            int rateCode = header[3] & 15;
            (int numerator, int denominator) = rateCode switch
            {
                1 => (24000, 1001),
                2 => (24, 1),
                3 => (25, 1),
                4 => (30000, 1001),
                5 => (30, 1),
                6 => (50, 1),
                7 => (60000, 1001),
                8 => (60, 1),
                _ => (0, 1)
            };
            if (width == 0 || height == 0 || numerator == 0)
            {
                throw new InvalidDataException("Invalid MPEG video sequence dimensions or frame rate.");
            }
            ReadOnlySpan<byte> extension = FindSequenceExtension(header);
            bool mpeg2 = !extension.IsEmpty;
            bool? progressive = true;
            VideoChromaSampling chroma = VideoChromaSampling.Sampling420;
            CodecProfileInfo? profile = null;
            if (mpeg2)
            {
                uint firstBits = BinaryPrimitives.ReadUInt32BigEndian(extension);
                width |= (int)((firstBits >> 15) & 3) << 12;
                height |= (int)((firstBits >> 13) & 3) << 12;
                progressive = ((firstBits >> 19) & 1) != 0;
                chroma = ((firstBits >> 17) & 3) switch
                {
                    1 => VideoChromaSampling.Sampling420,
                    2 => VideoChromaSampling.Sampling422,
                    3 => VideoChromaSampling.Sampling444,
                    _ => throw new InvalidDataException("Invalid MPEG-2 chroma format.")
                };
                if ((firstBits & (1U << 27)) == 0)
                {
                    string? name = ((firstBits >> 24) & 7) switch
                    {
                        1 => "High",
                        2 => "Spatially Scalable",
                        3 => "SNR Scalable",
                        4 => "Main",
                        5 => "Simple",
                        _ => null
                    };
                    string? level = ((firstBits >> 20) & 15) switch
                    {
                        4 => "High",
                        6 => "High 1440",
                        8 => "Main",
                        10 => "Low",
                        _ => null
                    };
                    if (name is not null) { profile = new(name, level); }
                }
                numerator *= 1 + ((extension[5] >> 5) & 3);
                denominator *= 1 + (extension[5] & 31);
            }
            SampleAspectRatio? sar = aspect switch
            {
                1 => SampleAspectRatio.Square,
                2 when mpeg2 => new(4 * height, 3 * width),
                3 when mpeg2 => new(16 * height, 9 * width),
                4 when mpeg2 => new(221 * height, 100 * width),
                _ => null
            };
            VideoStreamConfiguration description = new(width, height, chroma, 8, 8, progressive, sar)
            {
                CodecProfile = profile,
                PixelFormat = chroma switch
                {
                    VideoChromaSampling.Sampling420 => VideoPixelFormat.Yuv420P8,
                    VideoChromaSampling.Sampling422 => VideoPixelFormat.Yuv422P8,
                    VideoChromaSampling.Sampling444 => VideoPixelFormat.Yuv444P8,
                    _ => throw new InvalidOperationException("Unexpected MPEG video chroma sampling.")
                }
            };
            return new(mpeg2, new(numerator, denominator), new([description]));
        }

        private static ReadOnlySpan<byte> FindSequenceExtension(ReadOnlySpan<byte> data)
        {
            for (int offset = 8; offset <= data.Length - 4; offset++)
            {
                if (data[offset] != 0 || data[offset + 1] != 0 || data[offset + 2] != 1) { continue; }
                if (data[offset + 3] == 0xb5 && offset <= data.Length - 10 && data[offset + 4] >> 4 == 1)
                {
                    return data.Slice(offset + 4, 6);
                }
                break;
            }
            return [];
        }
    }
}
