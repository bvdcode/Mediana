// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Pdf
{
    internal class PdfValue(PdfValueKind kind)
    {
        internal PdfValueKind Kind { get; } = kind;
        internal double Number { get; init; }
        internal string Text { get; init; } = string.Empty;
        internal byte[] Bytes { get; init; } = [];
        internal PdfValue[] Items { get; init; } = [];
        internal Dictionary<string, PdfValue> Entries { get; init; } = new(StringComparer.Ordinal);
        internal int ObjectNumber { get; init; }
        internal int Generation { get; init; }
        internal static PdfValue Null { get; } = new(PdfValueKind.Null);
        internal PdfValue Get(string name) => Entries.GetValueOrDefault(name, Null);
        internal int Integer()
        {
            if (Kind != PdfValueKind.Number || Number != Math.Truncate(Number) || Number < int.MinValue || Number > int.MaxValue)
            { throw new InvalidDataException("Expected a PDF integer."); }
            return (int)Number;
        }
        internal double Numeric() => Kind == PdfValueKind.Number ? Number : throw new InvalidDataException("Expected a PDF number.");
        internal string Name() => Kind == PdfValueKind.Name ? Text : throw new InvalidDataException("Expected a PDF name.");
    }
}
