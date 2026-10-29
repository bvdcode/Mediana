// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text;

namespace Mediana.Containers.Flac
{
    internal ref struct FlacMetadataCursor(ReadOnlySpan<byte> data, int maxTextBytes)
    {
        private static readonly UTF8Encoding utf8 = new(false, true);
        private readonly ReadOnlySpan<byte> data = data;
        public int Position { get; private set; }
        public int Remaining => data.Length - Position;

        public uint ReadUInt32(bool littleEndian = false)
        {
            ReadOnlySpan<byte> bytes = Read(4);
            return littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : BinaryPrimitives.ReadUInt32BigEndian(bytes);
        }

        public ReadOnlySpan<byte> Read(int count)
        {
            if (count < 0 || count > Remaining)
            {
                throw new InvalidDataException("The FLAC metadata value extends past its block.");
            }
            ReadOnlySpan<byte> result = data.Slice(Position, count);
            Position += count;
            return result;
        }

        public string ReadText(bool littleEndian = false, bool ascii = false)
        {
            uint count = ReadUInt32(littleEndian);
            if (count > maxTextBytes)
            {
                throw new NotSupportedException("The FLAC text value exceeds its configured byte limit.");
            }
            ReadOnlySpan<byte> bytes = Read((int)count);
            if (ascii && bytes.ContainsAnyExceptInRange((byte)0x20, (byte)0x7e))
            {
                throw new InvalidDataException("The FLAC media type must contain printable ASCII.");
            }
            return utf8.GetString(bytes);
        }

        public void RequireEnd()
        {
            if (Remaining != 0)
            {
                throw new InvalidDataException("The FLAC metadata block contains unexpected trailing bytes.");
            }
        }
    }
}
