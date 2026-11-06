// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
namespace Mediana.Documents.Pdf
{
    internal static class PdfCharacterNames
    {
        internal static int GlyphCode(string name)
        {
            if (name.Length == 1) { return name[0]; }
            if (name.StartsWith("uni", StringComparison.Ordinal) && name.Length == 7 && int.TryParse(name.AsSpan(3), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code)) { return code; }
            return name switch
            {
                "space" => 32,
                "exclam" => 33,
                "quotedbl" => 34,
                "numbersign" => 35,
                "dollar" => 36,
                "percent" => 37,
                "ampersand" => 38,
                "quotesingle" => 39,
                "parenleft" => 40,
                "parenright" => 41,
                "asterisk" => 42,
                "plus" => 43,
                "comma" => 44,
                "hyphen" => 45,
                "period" => 46,
                "slash" => 47,
                "zero" => 48,
                "one" => 49,
                "two" => 50,
                "three" => 51,
                "four" => 52,
                "five" => 53,
                "six" => 54,
                "seven" => 55,
                "eight" => 56,
                "nine" => 57,
                "colon" => 58,
                "semicolon" => 59,
                "less" => 60,
                "equal" => 61,
                "greater" => 62,
                "question" => 63,
                "at" => 64,
                "bracketleft" => 91,
                "backslash" => 92,
                "bracketright" => 93,
                "asciicircum" => 94,
                "underscore" => 95,
                "grave" => 96,
                "braceleft" => 123,
                "bar" => 124,
                "braceright" => 125,
                "asciitilde" => 126,
                _ => -1
            };
        }
        internal static int WinAnsi(int code)
        {
            const string specials = "€\u0081‚ƒ„…†‡ˆ‰Š‹Œ\u008dŽ\u008f\u0090‘’“”•–—˜™š›œ\u009džŸ";
            return code is >= 128 and < 160 ? specials[code - 128] : code;
        }
    }
}
