// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Containers.MpegTs
{
    internal readonly record struct MpegPesHeader(int StreamId, int HeaderLength, int? PayloadLength, long? Pts, long? Dts)
    {
        public const int MaximumHeaderLength = 264;

        public static MpegPesHeader Read(ReadOnlySpan<byte> data)
        {
            if (data.Length < 7 || !data[..3].SequenceEqual<byte>([0, 0, 1]))
            {
                throw new InvalidDataException("Invalid MPEG packetized elementary stream header.");
            }

            int length = BinaryPrimitives.ReadUInt16BigEndian(data[4..]);
            int offset = 6;
            long? pts = null;
            long? dts = null;
            if ((data[offset] & 0xC0) == 0x80)
            {
                Require(data, 9);
                if ((data[6] & 0x30) != 0)
                {
                    throw new NotSupportedException("Scrambled MPEG PES payloads are not supported.");
                }
                offset = 9 + data[8];
                Require(data, offset);
                switch (data[7] >> 6)
                {
                    case 0:
                        break;
                    case 2:
                        pts = ReadTimestamp(data[..offset], 9, 2);
                        dts = pts;
                        break;
                    case 3:
                        pts = ReadTimestamp(data[..offset], 9, 3);
                        dts = ReadTimestamp(data[..offset], 14, 1);
                        break;
                    default:
                        throw new InvalidDataException("Invalid MPEG PES timestamp flags.");
                }
            }
            else
            {
                while (offset < data.Length && data[offset] == 0xFF && offset < 22)
                {
                    offset++;
                }
                Require(data, offset + 1);
                if ((data[offset] & 0xC0) == 0x40)
                {
                    offset += 2;
                    Require(data, offset + 1);
                }
                switch (data[offset] >> 4)
                {
                    case 2:
                        pts = ReadTimestamp(data, offset, 2);
                        dts = pts;
                        offset += 5;
                        break;
                    case 3:
                        pts = ReadTimestamp(data, offset, 3);
                        dts = ReadTimestamp(data, offset + 5, 1);
                        offset += 10;
                        break;
                    case 0 when data[offset] == 0x0F:
                        offset++;
                        break;
                    default:
                        throw new InvalidDataException("Invalid MPEG-1 PES header.");
                }
            }
            if (length != 0 && length < offset - 6)
            {
                throw new InvalidDataException("The MPEG PES header exceeds its packet length.");
            }
            return new(data[3], offset, length == 0 ? null : length - (offset - 6), pts, dts);
        }

        private static long ReadTimestamp(ReadOnlySpan<byte> data, int offset, int prefix)
        {
            Require(data, offset + 5);
            ReadOnlySpan<byte> timestamp = data.Slice(offset, 5);
            if (timestamp[0] >> 4 != prefix || ((timestamp[0] & timestamp[2] & timestamp[4]) & 1) == 0)
            {
                throw new InvalidDataException("Invalid MPEG PES timestamp markers.");
            }
            return ((long)(timestamp[0] & 0x0E) << 29) | ((long)timestamp[1] << 22) |
                ((long)(timestamp[2] & 0xFE) << 14) | ((long)timestamp[3] << 7) | (uint)(timestamp[4] >> 1);
        }

        private static void Require(ReadOnlySpan<byte> data, int length)
        {
            if (data.Length < length)
            {
                throw new InvalidDataException("Truncated MPEG PES header.");
            }
        }
    }
}
