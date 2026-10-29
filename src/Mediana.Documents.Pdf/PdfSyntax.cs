// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using System.Text;

namespace Mediana.Documents.Pdf
{
    internal class PdfSyntax(ReadOnlyMemory<byte> data, PdfBudget budget, CancellationToken token)
    {
        internal int Position { get; set; }
        internal ReadOnlyMemory<byte> Data { get; } = data;
        internal void Skip()
        {
            while (Position < Data.Length)
            {
                byte value = Data.Span[Position];
                if (White(value)) { Position++; }
                else if (value == '%')
                { while (Position < Data.Length && Data.Span[Position] is not (10 or 13)) { Position++; } }
                else { break; }
            }
        }
        internal static bool White(byte value) => value is 0 or 9 or 10 or 12 or 13 or 32;
        private static bool Delimiter(byte value) => White(value) || value is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';
        internal string Word()
        {
            Skip();
            int start = Position;
            while (Position < Data.Length && !Delimiter(Data.Span[Position])) { Position++; }
            if (start == Position) { throw new InvalidDataException("Missing PDF token."); }
            return Encoding.ASCII.GetString(Data.Span[start..Position]);
        }
        internal void Expect(string text)
        { if (Word() != text) { throw new InvalidDataException("Expected PDF token: " + text); } }
        internal PdfValue Read(int depth = 0)
        {
            budget.Value(depth, token);
            Skip();
            if (Position >= Data.Length) { throw new EndOfStreamException("Truncated PDF value."); }
            byte first = Data.Span[Position++];
            if (first == '/')
            {
                List<byte> name = [];
                while (Position < Data.Length && !Delimiter(Data.Span[Position]))
                {
                    byte c = Data.Span[Position++];
                    if (c == '#')
                    {
                        if (Position + 2 > Data.Length) { throw new InvalidDataException("Truncated PDF name escape."); }
                        c = (byte)((Hex(Data.Span[Position]) << 4) | Hex(Data.Span[Position + 1])); Position += 2;
                    }
                    name.Add(c);
                }
                return new(PdfValueKind.Name) { Text = Encoding.Latin1.GetString(name.ToArray()) };
            }
            if (first == '(') { return new(PdfValueKind.String) { Bytes = Literal() }; }
            if (first == '[')
            {
                List<PdfValue> items = [];
                while (true)
                {
                    Skip();
                    if (Position >= Data.Length) { throw new EndOfStreamException("Unterminated PDF array."); }
                    if (Data.Span[Position] == ']') { Position++; break; }
                    items.Add(Read(depth + 1));
                }
                return new(PdfValueKind.Array) { Items = items.ToArray() };
            }
            if (first == '<')
            {
                if (Position < Data.Length && Data.Span[Position] == '<')
                {
                    Position++;
                    Dictionary<string, PdfValue> dictionary = new(StringComparer.Ordinal);
                    while (true)
                    {
                        Skip();
                        if (Position + 1 < Data.Length && Data.Span[Position] == '>' && Data.Span[Position + 1] == '>') { Position += 2; break; }
                        string key = Read(depth + 1).Name();
                        if (!dictionary.TryAdd(key, Read(depth + 1))) { throw new InvalidDataException("Duplicate PDF dictionary key."); }
                    }
                    return new(PdfValueKind.Dictionary) { Entries = dictionary };
                }
                List<byte> bytes = [];
                int high = -1;
                while (true)
                {
                    if (Position >= Data.Length) { throw new EndOfStreamException("Unterminated PDF hexadecimal string."); }
                    byte c = Data.Span[Position++];
                    if (c == '>') { break; }
                    if (White(c)) { continue; }
                    int value = Hex(c);
                    if (high < 0) { high = value; }
                    else { bytes.Add((byte)((high << 4) | value)); high = -1; }
                }
                if (high >= 0) { bytes.Add((byte)(high << 4)); }
                return new(PdfValueKind.String) { Bytes = bytes.ToArray() };
            }
            Position--;
            string word = Word();
            if (word == "null") { return PdfValue.Null; }
            if (word is "true" or "false") { return new(PdfValueKind.Boolean) { Number = word == "true" ? 1 : 0 }; }
            if (word[0] is '+' or '-' or '.' or >= '0' and <= '9')
            {
                if (!double.TryParse(word, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number))
                { throw new InvalidDataException("Invalid PDF number."); }
                int afterFirst = Position;
                Skip();
                if (number >= 0 && number <= int.MaxValue && number == Math.Truncate(number) && Position < Data.Length && Data.Span[Position] is >= (byte)'0' and <= (byte)'9')
                {
                    string generation = Word();
                    int afterSecond = Position;
                    Skip();
                    if (Position < Data.Length && Data.Span[Position] == 'R' && (Position + 1 == Data.Length || Delimiter(Data.Span[Position + 1])))
                    {
                        Position++;
                        if (!int.TryParse(generation, NumberStyles.None, CultureInfo.InvariantCulture, out int gen) || gen > 65535) { throw new InvalidDataException("Invalid PDF reference generation."); }
                        return new(PdfValueKind.Reference) { ObjectNumber = (int)number, Generation = gen };
                    }
                    Position = afterSecond;
                }
                Position = afterFirst;
                return new(PdfValueKind.Number) { Number = number };
            }
            return new(PdfValueKind.Keyword) { Text = word };
        }
        private byte[] Literal()
        {
            List<byte> bytes = [];
            int nesting = 1;
            while (Position < Data.Length)
            {
                byte c = Data.Span[Position++];
                if (c == '\\')
                {
                    if (Position >= Data.Length) { break; }
                    c = Data.Span[Position++];
                    if (c is 10 or 13)
                    { if (c == 13 && Position < Data.Length && Data.Span[Position] == 10) { Position++; } continue; }
                    if (c is >= (byte)'0' and <= (byte)'7')
                    {
                        int value = c - '0';
                        for (int i = 0; i < 2 && Position < Data.Length && Data.Span[Position] is >= (byte)'0' and <= (byte)'7'; i++) { value = (value << 3) | (Data.Span[Position++] - '0'); }
                        bytes.Add((byte)value); continue;
                    }
                    c = c switch { (byte)'n' => 10, (byte)'r' => 13, (byte)'t' => 9, (byte)'b' => 8, (byte)'f' => 12, _ => c };
                }
                else if (c == '(') { nesting++; }
                else if (c == ')' && --nesting == 0) { return bytes.ToArray(); }
                else if (c == 13)
                { if (Position < Data.Length && Data.Span[Position] == 10) { Position++; } c = 10; }
                bytes.Add(c);
            }
            throw new EndOfStreamException("Unterminated PDF literal string.");
        }
        internal static int Hex(byte c) => c switch
        { >= (byte)'0' and <= (byte)'9' => c - '0', >= (byte)'a' and <= (byte)'f' => c - 'a' + 10, >= (byte)'A' and <= (byte)'F' => c - 'A' + 10, _ => throw new InvalidDataException("Invalid hexadecimal digit.") };
    }
}
