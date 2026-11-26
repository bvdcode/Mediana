// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Containers.Asf
{
    internal static class AsfPacketParser
    {
        public static int Parse(ReadOnlySpan<byte> data, long start, int packetMinimum, int packetMaximum, Action<AsfPayload> receive)
        {
            int cursor = 0;
            if ((data[0] & 0x80) != 0)
            {
                int correction = Take(data, ref cursor, 1)[0];
                if ((correction & 0x70) != 0) { throw new NotSupportedException("Unsupported ASF error correction layout."); }
                Take(data, ref cursor, correction & 15);
            }
            ReadOnlySpan<byte> flags = Take(data, ref cursor, 2);
            int lengths = flags[0];
            int properties = flags[1];
            if ((properties >> 6) != 1) { throw new InvalidDataException("ASF stream numbers must be one byte."); }

            int length = ReadField(data, ref cursor, lengths >> 5 & 3);
            if (length == 0) { length = packetMinimum; }
            ReadField(data, ref cursor, lengths >> 1 & 3);
            int padding = ReadField(data, ref cursor, lengths >> 3 & 3);
            Take(data, ref cursor, 6);
            if (length < packetMinimum)
            {
                padding = checked(padding + packetMinimum - length);
                length = packetMinimum;
            }
            if (length > packetMaximum || length > data.Length || padding > length - cursor)
            {
                throw new InvalidDataException("ASF packet length or padding is invalid.");
            }
            data = data[..(length - padding)];
            bool multiple = (lengths & 1) != 0;
            int payloadCount = 1;
            int payloadLengthWidth = 0;
            if (multiple)
            {
                int layout = Take(data, ref cursor, 1)[0];
                payloadCount = layout & 63;
                payloadLengthWidth = layout >> 6;
                if (payloadCount == 0 || payloadLengthWidth == 0)
                {
                    throw new InvalidDataException("Invalid ASF multiple-payload packet.");
                }
            }

            for (int index = 0; index < payloadCount; index++)
            {
                int streamFlags = Take(data, ref cursor, 1)[0];
                int stream = streamFlags & 0x7f;
                bool keyFrame = (streamFlags & 0x80) != 0;
                int number = ReadField(data, ref cursor, properties >> 4 & 3);
                int offset = ReadField(data, ref cursor, properties >> 2 & 3);
                int replicatedLength = ReadField(data, ref cursor, properties & 3);
                ReadOnlySpan<byte> replicated = Take(data, ref cursor, replicatedLength);
                int payloadLength = multiple ? ReadField(data, ref cursor, payloadLengthWidth) : data.Length - cursor;
                long position = start + cursor;
                ReadOnlySpan<byte> payload = Take(data, ref cursor, payloadLength);
                if (stream == 0) { throw new InvalidDataException("Invalid ASF payload stream number."); }
                if (replicatedLength == 1)
                {
                    ParseCompressed(payload, position, stream, number, offset, replicated[0], keyFrame, receive);
                }
                else if (replicatedLength is 0 or >= 8)
                {
                    int? size = replicatedLength == 0 ? null : ToLength(BinaryPrimitives.ReadUInt32LittleEndian(replicated));
                    long? time = replicatedLength == 0 ? null : BinaryPrimitives.ReadUInt32LittleEndian(replicated[4..]);
                    receive(new(stream, number, offset, size, time, keyFrame, position, payloadLength));
                }
                else
                {
                    throw new InvalidDataException("Invalid ASF replicated payload data length.");
                }
            }
            if (cursor != data.Length) { throw new InvalidDataException("Unexpected bytes following the ASF payloads."); }
            return length;
        }

        private static void ParseCompressed(ReadOnlySpan<byte> data, long position, int stream, int number, long time,
            int delta, bool keyFrame, Action<AsfPayload> receive)
        {
            int cursor = 0;
            while (cursor < data.Length)
            {
                int size = Take(data, ref cursor, 1)[0];
                int start = cursor;
                Take(data, ref cursor, size);
                if (size != 0) { receive(new(stream, number, 0, size, time, keyFrame, position + start, size)); }
                time += delta;
                number++;
            }
        }

        private static int ReadField(ReadOnlySpan<byte> data, ref int cursor, int width) => width switch
        {
            0 => 0,
            1 => Take(data, ref cursor, 1)[0],
            2 => BinaryPrimitives.ReadUInt16LittleEndian(Take(data, ref cursor, 2)),
            3 => ToLength(BinaryPrimitives.ReadUInt32LittleEndian(Take(data, ref cursor, 4))),
            _ => throw new ArgumentOutOfRangeException(nameof(width))
        };

        private static int ToLength(uint value) => value <= int.MaxValue ? (int)value :
            throw new NotSupportedException("An ASF packet field exceeds the supported integer width.");

        private static ReadOnlySpan<byte> Take(ReadOnlySpan<byte> source, ref int cursor, int count)
        {
            if (count > source.Length - cursor)
            {
                throw new InvalidDataException("An ASF packet field exceeds its payload.");
            }
            ReadOnlySpan<byte> result = source.Slice(cursor, count);
            cursor += count;
            return result;
        }
    }
}
