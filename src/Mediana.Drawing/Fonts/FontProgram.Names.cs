// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text;

namespace Mediana.Drawing.Fonts
{
    public partial class FontProgram
    {
        /// <summary>Gets the immutable decoded OpenType program owned by this reader.</summary>
        public ReadOnlyMemory<byte> Program => _data;

        /// <summary>Reads the typographic family (name ID16, then ID1), weight and italic flags. Null means no usable family name.</summary>
        public FontFaceInfo? ReadFaceInfo()
        {
            if (!_tables.TryGetValue("name", out (int Start, int Length) table) || table.Length < 6) { return null; }
            int count = U16(table.Start + 2), storage = U16(table.Start + 4);
            if (count > 4096 || 6 + count * 12 > table.Length || storage > table.Length) { throw new InvalidDataException("Invalid OpenType name table."); }
            string? family = null;
            int selected = -1;
            for (int i = 0; i < count; i++)
            {
                int record = table.Start + 6 + i * 12;
                int platform = U16(record), encoding = U16(record + 2), language = U16(record + 4), id = U16(record + 6);
                if (id is not (1 or 16) || !(platform == 0 || platform == 3 && encoding is 0 or 1 or 10)) { continue; }
                int length = U16(record + 8), offset = storage + U16(record + 10);
                if (length % 2 != 0 || offset > table.Length - length) { throw new InvalidDataException("Truncated OpenType family name."); }
                int score = (id == 16 ? 4 : 0) + (language == 0x409 ? 2 : 0) + (platform == 3 ? 1 : 0);
                if (score <= selected) { continue; }
                string name = Encoding.BigEndianUnicode.GetString(_data.AsSpan(table.Start + offset, length)).Trim();
                if (name.Length == 0) { continue; }
                family = name;
                selected = score;
            }
            if (family is null) { return null; }
            int head = Table("head", 54), style = U16(head + 44);
            int weight = (style & 1) != 0 ? 700 : 400;
            bool italic = (style & 2) != 0;
            if (_tables.TryGetValue("OS/2", out (int Start, int Length) os) && os.Length >= 64)
            {
                weight = U16(os.Start + 4);
                int slopeFlags = 1;
                if (U16(os.Start) >= 4) { slopeFlags |= 0x200; }
                italic = (U16(os.Start + 62) & slopeFlags) != 0;
            }
            return new(family, weight, italic);
        }
    }
}
