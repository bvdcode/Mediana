// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Formats.Audio
{
    /// <summary>Validates the exact extent, position table and header CRC of one complete ADTS transport frame.
    /// Raw block syntax and raw block CRCs require the codec parser.</summary>
    public readonly ref struct AdtsFrameLayout
    {
        private readonly ReadOnlySpan<byte> data;

        public AdtsFrameLayout(ReadOnlySpan<byte> data)
        {
            Header = AdtsFrameHeader.Read(data);
            if (Header.FrameBytes != data.Length)
            {
                throw new InvalidDataException("An ADTS packet must contain exactly one complete transport frame.");
            }
            this.data = data;
            if (Header.HasCrc && Header.RawDataBlockCount > 1)
            {
                ushort expected = BinaryPrimitives.ReadUInt16BigEndian(data[(Header.HeaderBytes - 2)..]);
                if (AdtsCrc16.Calculate(data[..(Header.HeaderBytes - 2)]) != expected)
                {
                    throw new InvalidDataException("The ADTS header CRC does not match its header and raw block positions.");
                }
                for (int block = 0; block < Header.RawDataBlockCount; block++)
                {
                    GetProtectedBlock(block);
                }
            }
        }

        public AdtsFrameHeader Header { get; }

        public AdtsRawDataBlock GetProtectedBlock(int index)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Header.RawDataBlockCount);
            if (!Header.HasCrc || Header.RawDataBlockCount == 1)
            {
                throw new InvalidOperationException("Only protected multi-block ADTS frames carry a raw block position table.");
            }
            int start = Header.HeaderBytes;
            if (index != 0)
            {
                start += ReadPosition(index);
            }
            int end = data.Length;
            if (index + 1 < Header.RawDataBlockCount)
            {
                end = Header.HeaderBytes + ReadPosition(index + 1);
            }
            if (start < Header.HeaderBytes || end > data.Length || end - start < 3)
            {
                throw new InvalidDataException("The ADTS raw block positions overlap or extend beyond the frame.");
            }
            return new(start, end - start - 2, BinaryPrimitives.ReadUInt16BigEndian(data[(end - 2)..]));
        }

        public void RequireConfiguration(AacConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            if (Header.AudioObjectType != 2 || Header.FrequencyIndex != configuration.FrequencyIndex
                || Header.ChannelConfiguration != configuration.ChannelConfiguration)
            {
                throw new InvalidDataException("The ADTS frame does not match the AAC core configuration.");
            }
        }

        private int ReadPosition(int index) => BinaryPrimitives.ReadUInt16BigEndian(
            data.Slice(AdtsFrameHeader.ByteLength + (index - 1) * 2, 2));
    }
}
