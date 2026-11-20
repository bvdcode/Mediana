// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text;

namespace Mediana.Codecs.OpenExr
{
    internal ref struct ExrCursor(ReadOnlyMemory<byte> data)
    {
        internal int Position { get; private set; }
        internal readonly int Remaining => data.Length - Position;
        internal byte Byte() => Take(1).Span[0];
        internal int Int() => BinaryPrimitives.ReadInt32LittleEndian(Take(4).Span);
        internal float Float() => BinaryPrimitives.ReadSingleLittleEndian(Take(4).Span);
        internal ReadOnlyMemory<byte> Take(int count)
        {
            if (count < 0 || count > Remaining) { throw new InvalidDataException("OpenEXR field exceeds its input."); }
            ReadOnlyMemory<byte> result = data.Slice(Position, count); Position += count; return result;
        }
        internal string Text(int maximum)
        {
            ReadOnlySpan<byte> bytes = data.Span[Position..];
            int length = bytes.IndexOf((byte)0);
            if (length < 0 || length > maximum) { throw new InvalidDataException("Invalid OpenEXR name length or terminator."); }
            string text = new UTF8Encoding(false, true).GetString(bytes[..length]);
            Position += length + 1;
            return text;
        }
    }
}
