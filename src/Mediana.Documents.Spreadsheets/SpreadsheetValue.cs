// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;

namespace Mediana.Documents.Spreadsheets
{
    /// <summary>A discriminated cell value. Default is Blank; accessors reject a different value kind.</summary>
    public readonly record struct SpreadsheetValue
    {
        private readonly string? _text;
        private readonly double _number;
        private readonly bool _boolean;
        private readonly SpreadsheetDateTime _date;
        public SpreadsheetValueKind Kind { get; }
        private SpreadsheetValue(SpreadsheetValueKind kind, string? text = null, double number = 0, bool boolean = false,
            SpreadsheetDateTime date = default)
        { Kind = kind; _text = text; _number = number; _boolean = boolean; _date = date; }
        public string Text => Kind == SpreadsheetValueKind.Text ? _text! : throw WrongKind();
        public double Number => Kind == SpreadsheetValueKind.Number ? _number : throw WrongKind();
        public bool Boolean => Kind == SpreadsheetValueKind.Boolean ? _boolean : throw WrongKind();
        public string Error => Kind == SpreadsheetValueKind.Error ? _text! : throw WrongKind();
        public SpreadsheetDateTime DateTime => Kind == SpreadsheetValueKind.DateTime ? _date : throw WrongKind();
        public static SpreadsheetValue FromText(string text) { ArgumentNullException.ThrowIfNull(text); return new(SpreadsheetValueKind.Text, text); }
        public static SpreadsheetValue FromError(string error) { ArgumentException.ThrowIfNullOrWhiteSpace(error); return new(SpreadsheetValueKind.Error, error); }
        public static SpreadsheetValue FromBoolean(bool value) => new(SpreadsheetValueKind.Boolean, boolean: value);
        public static SpreadsheetValue FromDateTime(SpreadsheetDateTime value) => new(SpreadsheetValueKind.DateTime, date: value);
        public static SpreadsheetValue FromNumber(double value)
        {
            if (!double.IsFinite(value)) { throw new InvalidDataException("Non-finite spreadsheet number."); }
            return new(SpreadsheetValueKind.Number, number: value);
        }
        public override string ToString() => Kind switch
        {
            SpreadsheetValueKind.Blank => "",
            SpreadsheetValueKind.Text or SpreadsheetValueKind.Error => _text!,
            SpreadsheetValueKind.Number => _number.ToString("R", CultureInfo.InvariantCulture),
            SpreadsheetValueKind.Boolean => _boolean ? "TRUE" : "FALSE",
            SpreadsheetValueKind.DateTime => _date.Iso8601 ?? _date.Value?.ToString("O", CultureInfo.InvariantCulture)
                ?? _date.SerialValue!.Value.ToString("R", CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException("Unknown spreadsheet value kind.")
        };
        private InvalidOperationException WrongKind() => new("The cell contains " + Kind + ".");
    }
}
