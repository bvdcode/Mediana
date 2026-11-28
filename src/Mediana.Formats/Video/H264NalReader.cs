// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Formats.Video
{
    internal struct H264NalReader
    {
        internal static ReadOnlySpan<byte> StartCode => [0, 0, 1];

        internal static bool StartsWithAnnexB(ReadOnlySpan<byte> data)
        {
            int start = data.IndexOf(StartCode);
            return start >= 0 && data[..start].IndexOfAnyExcept((byte)0) < 0;
        }

        private readonly ReadOnlyMemory<byte> data;
        private readonly int lengthSize;
        private readonly int maximumNals;
        private int offset;
        private int count;

        public H264NalReader(ReadOnlyMemory<byte> data, int lengthSize, int maximumNals)
        {
            if (lengthSize is not (0 or 1 or 2 or 4))
            {
                throw new ArgumentOutOfRangeException(nameof(lengthSize));
            }
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumNals);
            this.data = data;
            this.lengthSize = lengthSize;
            this.maximumNals = maximumNals;
        }

        public bool TryRead(out ReadOnlyMemory<byte> nal)
        {
            nal = default;
            if (offset == data.Length)
            {
                return false;
            }
            if (count == maximumNals)
            {
                throw new NotSupportedException("The AVC packet exceeds the configured NAL count limit.");
            }
            if (lengthSize == 0)
            {
                nal = ReadAnnexB();
                count++;
                return true;
            }
            if (data.Length - offset < lengthSize)
            {
                throw new InvalidDataException("The AVC NAL length is truncated.");
            }
            ReadOnlySpan<byte> remaining = data.Span[offset..];
            uint length = ReadLength(remaining, lengthSize);
            int start = offset + lengthSize;
            if (length == 0 || length > data.Length - start)
            {
                throw new InvalidDataException("The AVC NAL is truncated or empty.");
            }
            nal = data.Slice(start, (int)length);
            offset = start + (int)length;
            count++;
            return true;
        }

        internal static uint ReadLength(ReadOnlySpan<byte> bytes, int lengthSize) => lengthSize switch
        {
            1 => bytes[0],
            2 => BinaryPrimitives.ReadUInt16BigEndian(bytes),
            4 => BinaryPrimitives.ReadUInt32BigEndian(bytes),
            _ => throw new ArgumentOutOfRangeException(nameof(lengthSize))
        };

        private ReadOnlyMemory<byte> ReadAnnexB()
        {
            ReadOnlySpan<byte> prefix = StartCode;
            ReadOnlySpan<byte> remaining = data.Span[offset..];
            int startCode = remaining.IndexOf(prefix);
            if (startCode < 0 || remaining[..startCode].IndexOfAnyExcept((byte)0) >= 0)
            {
                throw new InvalidDataException("The H.264 Annex B start code is missing.");
            }
            int start = offset + startCode + prefix.Length;
            ReadOnlySpan<byte> nalAndRest = data.Span[start..];
            int next = nalAndRest.IndexOf(prefix);
            int end = next < 0 ? data.Length : start + next;
            int bytes = data.Span[start..end].TrimEnd((byte)0).Length;
            if (bytes == 0)
            {
                throw new InvalidDataException("The H.264 Annex B NAL unit is empty.");
            }
            offset = end;
            return data.Slice(start, bytes);
        }
    }
}
