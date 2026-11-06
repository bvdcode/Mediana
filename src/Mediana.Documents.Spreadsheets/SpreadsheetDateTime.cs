// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Xml;

namespace Mediana.Documents.Spreadsheets
{
    /// <summary>Preserves an Excel serial or an ISO 8601 value. Fictitious or out-of-range dates retain their serial with Value=null.</summary>
    public readonly record struct SpreadsheetDateTime
    {
        public double? SerialValue { get; }
        public SpreadsheetDateSystem DateSystem { get; }
        public string? Iso8601 { get; }
        public DateTime? Value { get; }

        public SpreadsheetDateTime(double serial, SpreadsheetDateSystem dateSystem)
        {
            if (!double.IsFinite(serial)) { throw new InvalidDataException("Non-finite spreadsheet date serial."); }
            DateTime epoch = dateSystem switch
            {
                SpreadsheetDateSystem.Excel1900 => new(1899, 12, 31),
                SpreadsheetDateSystem.Excel1904 => new(1904, 1, 1),
                _ => throw new ArgumentOutOfRangeException(nameof(dateSystem))
            };
            SerialValue = serial;
            DateSystem = dateSystem;
            Iso8601 = null;
            if (dateSystem == SpreadsheetDateSystem.Excel1900 && serial is >= 60 and < 61) { Value = null; return; }
            double days = dateSystem == SpreadsheetDateSystem.Excel1900 && serial >= 61 ? serial - 1 : serial;
            double ticks = days * TimeSpan.TicksPerDay;
            if (ticks < DateTime.MinValue.Ticks - epoch.Ticks || ticks > DateTime.MaxValue.Ticks - epoch.Ticks)
            { Value = null; return; }
            long absolute = checked(epoch.Ticks + (long)Math.Round(ticks));
            Value = absolute >= DateTime.MinValue.Ticks && absolute <= DateTime.MaxValue.Ticks
                ? new DateTime(absolute, DateTimeKind.Unspecified) : null;
        }

        public SpreadsheetDateTime(string iso8601)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(iso8601);
            Value = XmlConvert.ToDateTime(iso8601, XmlDateTimeSerializationMode.RoundtripKind);
            Iso8601 = iso8601;
            SerialValue = null;
            DateSystem = SpreadsheetDateSystem.Excel1900;
        }
    }
}
