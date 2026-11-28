// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Formats.Video
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

        internal static byte[] Write(int lengthSize, IReadOnlyCollection<byte[]> sequences, IReadOnlyCollection<byte[]> pictures)
        {
            if (lengthSize is not (1 or 2 or 4) || sequences.Count is 0 or > 31 || pictures.Count is 0 or > 255)
            {
                throw new InvalidDataException("The AVC configuration cannot represent these parameter sets.");
            }
            int length = checked(7 + sequences.Sum(set => set.Length + 2) + pictures.Sum(set => set.Length + 2));
            if (length > DefaultMaxConfigurationBytes)
            {
                throw new NotSupportedException("The AVC configuration exceeds the configured byte limit.");
            }
            byte[] result = new byte[length];
            result[0] = 1;
            sequences.First().AsSpan(1, 3).CopyTo(result.AsSpan(1));
            result[4] = (byte)(0xfc | (lengthSize - 1));
            result[5] = (byte)(0xe0 | sequences.Count);
            int offset = WriteParameterSets(sequences, result, 6);
            result[offset++] = (byte)pictures.Count;
            WriteParameterSets(pictures, result, offset);
            return result;
        }

        private static int WriteParameterSets(IReadOnlyCollection<byte[]> sets, Span<byte> output, int offset)
        {
            foreach (byte[] set in sets)
            {
                BinaryPrimitives.WriteUInt16BigEndian(output[offset..], checked((ushort)set.Length));
                set.CopyTo(output[(offset + 2)..]);
                offset += 2 + set.Length;
            }
            return offset;
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
