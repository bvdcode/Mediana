// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Aac
{
    internal ref struct AacAdtsCrc
    {
        private readonly ReadOnlySpan<byte> payload;
        private readonly bool present;
        private readonly ushort expected;
        private ushort remainder;

        public AacAdtsCrc(ReadOnlySpan<byte> payload, ReadOnlySpan<byte> prefix, int frequencyIndex, int channels,
            ushort? rawDataBlockCrc = null)
        {
            this.payload = payload;
            remainder = AdtsCrc16.Initial;
            if (rawDataBlockCrc.HasValue)
            {
                if (!prefix.IsEmpty)
                {
                    throw new InvalidDataException("An ADTS raw block CRC cannot be combined with a single-block header CRC.");
                }
                present = true;
                expected = rawDataBlockCrc.Value;
                return;
            }
            if (prefix.IsEmpty)
            {
                return;
            }
            if (prefix.Length != AdtsPacketProtection.PrefixByteLength)
            {
                throw new InvalidDataException("The ADTS protection prefix must contain one header and CRC word.");
            }
            AdtsFrameHeader header = AdtsFrameHeader.Read(prefix);
            int channelCount = header.ChannelConfiguration == 7 ? 8 : header.ChannelConfiguration;
            if (!header.HasCrc || header.RawDataBlockCount != 1 || header.FrameBytes != prefix.Length + payload.Length
                || header.AudioObjectType != 2 || header.FrequencyIndex != frequencyIndex || channelCount != channels)
            {
                throw new InvalidDataException("The ADTS protection header does not match the AAC packet configuration and size.");
            }
            present = true;
            expected = BinaryPrimitives.ReadUInt16BigEndian(prefix[AdtsFrameHeader.ByteLength..]);
            foreach (byte value in prefix[..AdtsFrameHeader.ByteLength])
            {
                Append(value, 8);
            }
        }

        public void AddChannel(int start, int end) => AddRegion(start, end, 192);
        public void AddSecondChannel(int start, int end) => AddRegion(start, end, 128);
        public void AddDataElement(int start, int end) => AddRegion(start, end, end - start);

        public readonly void Verify()
        {
            if (present && remainder != expected)
            {
                throw new InvalidDataException("The ADTS packet CRC does not match its protected header and AAC syntax regions.");
            }
        }

        private void AddRegion(int start, int end, int protectedBits)
        {
            if (!present)
            {
                return;
            }
            AacBitReader bits = new(payload);
            bits.Skip(start);
            int available = Math.Min(end - start, protectedBits);
            int padding = protectedBits - available;
            while (available > 0)
            {
                int count = Math.Min(8, available);
                Append(bits.Read(count), count);
                available -= count;
            }
            while (padding > 0)
            {
                int count = Math.Min(8, padding);
                Append(0, count);
                padding -= count;
            }
        }

        private void Append(int value, int count)
        {
            remainder = AdtsCrc16.Append(remainder, value, count);
        }
    }
}
