// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Codecs.H264
{
    internal ref struct H264ConfigurationRecord
    {
        internal const int DefaultMaxConfigurationBytes = 64 * 1024;
        internal const int DefaultMaxParameterSetBytes = 16 * 1024;

        private readonly ReadOnlySpan<byte> data;
        private readonly int maxParameterSetBytes;
        private int offset;

        public H264ConfigurationRecord(ReadOnlySpan<byte> data, int maxConfigurationBytes, int maxParameterSetBytes)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConfigurationBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxParameterSetBytes);
            if (data.Length > maxConfigurationBytes)
            {
                throw new NotSupportedException("The AVC configuration exceeds the configured byte limit.");
            }
            if (data.Length < 7 || data[0] != 1 || (data[4] & 0xFC) != 0xFC || (data[5] & 0xE0) != 0xE0)
            {
                throw new InvalidDataException("The AVC configuration record is invalid.");
            }
            LengthSize = (data[4] & 3) + 1;
            if (LengthSize == 3)
            {
                throw new InvalidDataException("The AVC NAL length size is reserved.");
            }
            SequenceCount = data[5] & 31;
            this.data = data;
            this.maxParameterSetBytes = maxParameterSetBytes;
            offset = 6;
        }

        public int LengthSize { get; }
        public int SequenceCount { get; }

        public ReadOnlySpan<byte> ReadNal()
        {
            if (data.Length - offset < 2)
            {
                throw new InvalidDataException("The AVC parameter set length is truncated.");
            }
            int length = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
            offset += 2;
            if (length == 0 || length > data.Length - offset)
            {
                throw new InvalidDataException("The AVC parameter set is truncated or empty.");
            }
            if (length > maxParameterSetBytes)
            {
                throw new NotSupportedException("The AVC parameter set exceeds the configured byte limit.");
            }
            ReadOnlySpan<byte> nal = data.Slice(offset, length);
            offset += length;
            return nal;
        }

        public int ReadPictureCount() => ReadByte();

        public void Finish()
        {
            if (offset < data.Length && data[1] is not (66 or 77 or 88))
            {
                int chroma = ReadByte();
                int lumaDepth = ReadByte();
                int chromaDepth = ReadByte();
                if ((chroma & 0xFC) != 0xFC || (lumaDepth & 0xF8) != 0xF8 || (chromaDepth & 0xF8) != 0xF8)
                {
                    throw new InvalidDataException("The AVC configuration extension reserved bits are invalid.");
                }
                if (ReadByte() != 0)
                {
                    throw new NotSupportedException("AVC sequence parameter extensions are not supported.");
                }
            }
            if (offset != data.Length)
            {
                throw new InvalidDataException("The AVC configuration contains unexpected bytes.");
            }
        }

        private int ReadByte()
        {
            if (offset == data.Length)
            {
                throw new InvalidDataException("The AVC configuration is truncated.");
            }
            return data[offset++];
        }
    }
}
