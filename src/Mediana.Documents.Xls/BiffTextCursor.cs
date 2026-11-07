// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Xls
{
    internal class BiffTextCursor(BiffRecordReader records, int offset)
    {
        private int _position = offset;
        internal int Remaining => records.Data.Length - _position;
        private void Fixed(int count)
        {
            if (Remaining < count) { throw new InvalidDataException("A BIFF string header crosses its record boundary."); }
        }
        private byte Byte()
        {
            if (Remaining == 0) { Continue(); }
            return records.Data.Span[_position++];
        }
        private void Continue()
        {
            records.Token.ThrowIfCancellationRequested();
            if (!records.MoveNext() || records.Type != BiffRecordType.Continue || records.Data.IsEmpty)
            { throw new InvalidDataException("Missing BIFF string CONTINUE record."); }
            _position = 0;
        }
        internal string ReadString(bool shortLength = false, bool rich = false, long maximumCharacters = long.MaxValue)
        {
            if (Remaining == 0) { Continue(); }
            Fixed(shortLength ? 2 : 3);
            int characters = shortLength ? Byte() : Byte() | Byte() << 8;
            if (characters > maximumCharacters) { throw new InvalidDataException("BIFF string exceeds its character limit."); }
            byte flags = Byte();
            int runs = 0, extended = 0;
            if (rich && (flags & 8) != 0) { Fixed(2); runs = Byte() | Byte() << 8; }
            if (rich && (flags & 4) != 0)
            {
                Fixed(4);
                uint bytes = (uint)(Byte() | Byte() << 8 | Byte() << 16 | Byte() << 24);
                if (bytes > int.MaxValue) { throw new InvalidDataException("Invalid BIFF phonetic-string length."); }
                extended = (int)bytes;
            }
            string value = string.Create(characters, (Cursor: this, Wide: (flags & 1) != 0),
                static (output, state) => state.Cursor.Characters(output, state.Wide));
            Skip((long)runs * 4 + extended);
            return value;
        }
        private void Characters(Span<char> output, bool wide)
        {
            for (int i = 0; i < output.Length; i++)
            {
                records.Token.ThrowIfCancellationRequested();
                if (Remaining == 0) { Continue(); wide = (Byte() & 1) != 0; }
                if (wide && Remaining < 2) { throw new InvalidDataException("A BIFF UTF-16 character crosses its record boundary."); }
                output[i] = wide ? (char)(Byte() | Byte() << 8) : (char)Byte();
            }
            for (int i = 0; i < output.Length; i++)
            {
                if (!char.IsSurrogate(output[i])) { continue; }
                if (!char.IsHighSurrogate(output[i]) || i + 1 == output.Length || !char.IsLowSurrogate(output[++i]))
                { throw new InvalidDataException("Invalid UTF-16 in BIFF text."); }
            }
        }
        private void Skip(long count)
        {
            while (count > 0)
            {
                if (Remaining == 0) { Continue(); }
                int bytes = (int)Math.Min(count, Remaining);
                _position += bytes;
                count -= bytes;
            }
        }
    }
}
