// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Audio;

namespace Mediana.Codecs.Aac
{
    internal ref struct AacAdtsBlockReader
    {
        private readonly ReadOnlySpan<byte> data;
        private readonly AdtsFrameLayout frame;
        private readonly int maximumBytes;
        private int offset;
        private int currentBytes;

        public AacAdtsBlockReader(ReadOnlySpan<byte> data, AacConfiguration configuration, int maximumBytes)
        {
            this.data = data;
            this.maximumBytes = maximumBytes;
            frame = new(data);
            frame.RequireConfiguration(configuration);
            offset = frame.Header.HeaderBytes;
        }

        public readonly int BlockCount => frame.Header.RawDataBlockCount;
        public readonly bool HasCrc => frame.Header.HasCrc;
        public int Index { get; private set; }

        public ReadOnlySpan<byte> Read(out ReadOnlySpan<byte> protection, out ushort? crc, out bool allowTrailingBlocks)
        {
            if (Index >= BlockCount)
            {
                throw new InvalidOperationException("All ADTS raw data blocks have been read.");
            }
            protection = default;
            crc = null;
            allowTrailingBlocks = !HasCrc && Index + 1 < BlockCount;
            if (HasCrc && BlockCount > 1)
            {
                AdtsRawDataBlock block = frame.GetProtectedBlock(Index);
                offset = block.Offset;
                currentBytes = block.ByteLength;
                crc = block.Crc;
            }
            else
            {
                currentBytes = data.Length - offset;
                if (HasCrc)
                {
                    protection = data[..offset];
                }
            }
            if (!allowTrailingBlocks && currentBytes > maximumBytes)
            {
                throw new NotSupportedException("The AAC raw data block exceeds the configured access unit limit.");
            }
            return data.Slice(offset, currentBytes);
        }

        public void Advance(int byteLength)
        {
            if (byteLength < 1 || byteLength > currentBytes)
            {
                throw new InvalidDataException("The AAC raw data block has an invalid byte extent.");
            }
            if (byteLength > maximumBytes)
            {
                throw new NotSupportedException("The AAC raw data block exceeds the configured access unit limit.");
            }
            offset += byteLength;
            if (HasCrc && BlockCount > 1)
            {
                offset += 2;
            }
            Index++;
            if (Index == BlockCount && offset != data.Length)
            {
                throw new InvalidDataException("The ADTS raw data blocks do not fill their transport frame.");
            }
        }
    }
}
