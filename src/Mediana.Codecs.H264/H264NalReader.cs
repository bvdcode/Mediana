// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Codecs.H264
{
    internal struct H264NalReader
    {
        private readonly ReadOnlyMemory<byte> data;
        private readonly int lengthSize;
        private readonly int maximumNals;
        private int offset;
        private int count;

        public H264NalReader(ReadOnlyMemory<byte> data, int lengthSize, int maximumNals)
        {
            if (lengthSize is not (1 or 2 or 4))
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
            if (data.Length - offset < lengthSize)
            {
                throw new InvalidDataException("The AVC NAL length is truncated.");
            }
            ReadOnlySpan<byte> remaining = data.Span[offset..];
            uint length = lengthSize switch
            {
                1 => remaining[0],
                2 => BinaryPrimitives.ReadUInt16BigEndian(remaining),
                4 => BinaryPrimitives.ReadUInt32BigEndian(remaining),
                _ => throw new InvalidOperationException("The AVC NAL length size is invalid.")
            };
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
    }
}
