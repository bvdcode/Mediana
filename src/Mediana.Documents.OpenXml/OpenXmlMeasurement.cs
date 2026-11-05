// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;

namespace Mediana.Documents.OpenXml
{
    /// <summary>Open XML integer and universal measurements expressed in typographic points.</summary>
    public static class OpenXmlMeasurement
    {
        /// <summary>Parses a finite measurement. Fractional implicit units require an explicit recovery policy.
        /// Universal units (mm, cm, in, pt, pc and pi) allow decimal values.</summary>
        public static double ToPoints(string value, OpenXmlNumericUnit implicitUnit, bool allowFractional = false,
            Action<string>? warningHandler = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            double scale = implicitUnit switch
            {
                OpenXmlNumericUnit.Twip => 1.0 / 20,
                OpenXmlNumericUnit.HalfPoint => 1.0 / 2,
                OpenXmlNumericUnit.EnglishMetricUnit => 1.0 / 12700,
                _ => throw new ArgumentOutOfRangeException(nameof(implicitUnit))
            };
            ReadOnlySpan<char> number = value.AsSpan().Trim();
            bool universal = number.Length >= 2 && char.IsAsciiLetter(number[^1]);
            if (universal)
            {
                scale = number[^2..] switch
                {
                    "mm" => 72.0 / 25.4,
                    "cm" => 72.0 / 2.54,
                    "in" => 72,
                    "pt" => 1,
                    "pc" or "pi" => 12,
                    _ => throw new InvalidDataException("Unknown Open XML measurement unit.")
                };
                number = number[..^2];
            }
            if (!double.TryParse(number, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out double parsed) || !double.IsFinite(parsed * scale))
            {
                throw new InvalidDataException("Invalid finite Open XML measurement.");
            }
            if (!universal && parsed != Math.Truncate(parsed))
            {
                if (!allowFractional) { throw new InvalidDataException("Fractional implicit Open XML measurements require AllowFractionalMeasurements."); }
                warningHandler?.Invoke("A fractional Open XML measurement was preserved without rounding.");
            }
            return parsed * scale;
        }
    }
}
