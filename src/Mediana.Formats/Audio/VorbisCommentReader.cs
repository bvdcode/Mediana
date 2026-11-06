// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text;

namespace Mediana.Formats.Audio
{
    /// <summary>Borrowed comment cursor. Reads one value at a time without an intermediate tag collection.</summary>
    public ref struct VorbisCommentReader
    {
        private static readonly UTF8Encoding Utf8 = new(false, true);
        private readonly ReadOnlySpan<byte> data;
        private readonly int maximumTextBytes;
        private uint read;
        private int position;

        public VorbisCommentReader(ReadOnlySpan<byte> data, int maximumTextBytes = int.MaxValue, int maximumComments = int.MaxValue)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumTextBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumComments);
            this.data = data;
            this.maximumTextBytes = maximumTextBytes;
            Vendor = ReadText();
            Count = ReadUInt32();
            if (Count > maximumComments) { throw new NotSupportedException("The Vorbis comment count exceeds the configured limit."); }
            if (Count > (data.Length - position) / sizeof(uint)) { throw new InvalidDataException("The Vorbis comment count exceeds its header."); }
        }

        public readonly string Vendor { get; }
        public readonly uint Count { get; }
        public readonly int ConsumedBytes => position;

        public bool TryRead(out VorbisComment comment, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (read == Count)
            {
                comment = default;
                return false;
            }
            string field = ReadText();
            int separator = field.IndexOf('=');
            if (separator <= 0 || field.AsSpan(0, separator).ContainsAnyExceptInRange(' ', '~'))
            {
                throw new InvalidDataException("A Vorbis comment requires a printable ASCII name followed by '='.");
            }
            comment = new(field[..separator], field[(separator + 1)..]);
            read++;
            return true;
        }

        private string ReadText()
        {
            uint length = ReadUInt32();
            if (length > maximumTextBytes) { throw new NotSupportedException("A Vorbis text value exceeds the configured byte limit."); }
            if (length > data.Length - position) { throw new InvalidDataException("A Vorbis text value extends past its header."); }
            string result = Utf8.GetString(data.Slice(position, (int)length));
            position += (int)length;
            return result;
        }

        private uint ReadUInt32()
        {
            if (data.Length - position < 4) { throw new InvalidDataException("A Vorbis comment length is truncated."); }
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(data[position..]);
            position += 4;
            return value;
        }
    }
}
