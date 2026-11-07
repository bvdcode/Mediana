// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Containers.Mp4
{
    internal class Mp4BoxWriter(Memory<byte> storage)
    {
        public int Position { get; private set; }
        public bool Measuring => storage.IsEmpty;

        public int Begin(string type)
        {
            int start = Position;
            UInt32(0);
            if (type.Length != 4 || type.Any(character => character > 127))
            {
                throw new ArgumentException("Box types require four ASCII characters.", nameof(type));
            }
            foreach (char character in type) { Byte((byte)character); }
            return start;
        }

        public int Full(string type, uint versionAndFlags = 0)
        {
            int start = Begin(type);
            UInt32(versionAndFlags);
            return start;
        }

        public void End(int start)
        {
            if (!Measuring) { BinaryPrimitives.WriteUInt32BigEndian(storage.Span[start..], checked((uint)(Position - start))); }
        }

        public void Byte(byte value)
        {
            if (!Measuring) { storage.Span[Position] = value; }
            Position = checked(Position + 1);
        }

        public void UInt32(uint value)
        {
            if (!Measuring) { BinaryPrimitives.WriteUInt32BigEndian(storage.Span[Position..], value); }
            Position = checked(Position + 4);
        }

        public void UInt16(ushort value)
        {
            if (!Measuring) { BinaryPrimitives.WriteUInt16BigEndian(storage.Span[Position..], value); }
            Position = checked(Position + 2);
        }

        public void Zeros(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (!Measuring) { storage.Span.Slice(Position, count).Clear(); }
            Position = checked(Position + count);
        }

        public void UInt64(ulong value)
        {
            if (!Measuring) { BinaryPrimitives.WriteUInt64BigEndian(storage.Span[Position..], value); }
            Position = checked(Position + 8);
        }

        public void Bytes(ReadOnlySpan<byte> value)
        {
            if (!Measuring) { value.CopyTo(storage.Span[Position..]); }
            Position = checked(Position + value.Length);
        }

        public void Box(string type, ReadOnlySpan<byte> payload)
        {
            int start = Begin(type);
            Bytes(payload);
            End(start);
        }

        public void HeaderWithoutDuration(Mp4Box box, int offset, int bytes)
        {
            int start = Begin(box.Type);
            Bytes(box.Payload.Span[..offset]);
            if (bytes == 8) { UInt64(0); }
            else if (bytes == 4) { UInt32(0); }
            else { throw new ArgumentOutOfRangeException(nameof(bytes)); }
            Bytes(box.Payload.Span[(offset + bytes)..]);
            End(start);
        }
    }
}
