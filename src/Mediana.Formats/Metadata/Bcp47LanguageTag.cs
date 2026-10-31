// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Metadata
{
    /// <summary>RFC 5646 well-formed syntax. Does not query the language-subtag registry or canonicalize identifiers.</summary>
    public static class Bcp47LanguageTag
    {
        public static bool IsWellFormed(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            ReadOnlySpan<char> text = value.AsSpan();
            foreach (Range range in text.Split('-'))
            {
                ReadOnlySpan<char> part = text[range];
                if (part.Length is < 1 or > 8 || !IsAlphanumeric(part))
                {
                    return false;
                }
            }
            if (value.Length <= 11 && IsGrandfathered(value.ToLowerInvariant()))
            {
                return true;
            }
            int position = 0;
            ReadOnlySpan<char> token = Next(text, ref position);
            if (IsPrivate(token))
            {
                return position <= text.Length;
            }
            if (token.Length is < 2 or > 8 || !IsAlphabetic(token))
            {
                return false;
            }
            int languageLength = token.Length;
            token = Next(text, ref position);
            if (languageLength <= 3)
            {
                for (int index = 0; index < 3 && token.Length == 3 && IsAlphabetic(token); index++)
                {
                    token = Next(text, ref position);
                }
            }
            if (token.Length == 4 && IsAlphabetic(token))
            {
                token = Next(text, ref position);
            }
            if ((token.Length == 2 && IsAlphabetic(token)) || (token.Length == 3 && IsNumeric(token)))
            {
                token = Next(text, ref position);
            }
            HashSet<string>? variants = null;
            while (token.Length is >= 5 and <= 8 || (token.Length == 4 && char.IsAsciiDigit(token[0])))
            {
                variants ??= new(StringComparer.OrdinalIgnoreCase);
                if (!variants.Add(token.ToString()))
                {
                    return false;
                }
                token = Next(text, ref position);
            }
            ulong extensions = 0;
            while (token.Length == 1 && !IsPrivate(token))
            {
                char singleton = char.ToLowerInvariant(token[0]);
                int index = char.IsAsciiDigit(singleton) ? singleton - '0' : singleton - 'a' + 10;
                ulong bit = 1UL << index;
                if ((extensions & bit) != 0)
                {
                    return false;
                }
                extensions |= bit;
                token = Next(text, ref position);
                if (token.Length < 2)
                {
                    return false;
                }
                do
                {
                    token = Next(text, ref position);
                } while (token.Length >= 2);
            }
            if (IsPrivate(token))
            {
                return position <= text.Length;
            }
            return token.IsEmpty;
        }

        private static ReadOnlySpan<char> Next(ReadOnlySpan<char> value, ref int position)
        {
            if (position > value.Length)
            {
                return [];
            }
            ReadOnlySpan<char> remaining = value[position..];
            int end = remaining.IndexOf('-');
            if (end < 0)
            {
                position = value.Length + 1;
                return remaining;
            }
            position += end + 1;
            return remaining[..end];
        }

        private static bool IsPrivate(ReadOnlySpan<char> value) => value.Length == 1 && value[0] is 'x' or 'X';

        private static bool IsAlphabetic(ReadOnlySpan<char> value)
        {
            foreach (char character in value)
            {
                if (!char.IsAsciiLetter(character))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsNumeric(ReadOnlySpan<char> value)
        {
            foreach (char character in value)
            {
                if (!char.IsAsciiDigit(character))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsAlphanumeric(ReadOnlySpan<char> value)
        {
            foreach (char character in value)
            {
                if (!char.IsAsciiLetterOrDigit(character))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsGrandfathered(string value) => value is
            "en-gb-oed" or "i-ami" or "i-bnn" or "i-default" or "i-enochian" or "i-hak" or "i-klingon" or
            "i-lux" or "i-mingo" or "i-navajo" or "i-pwn" or "i-tao" or "i-tay" or "i-tsu" or
            "sgn-be-fr" or "sgn-be-nl" or "sgn-ch-de" or "art-lojban" or "cel-gaulish" or "no-bok" or
            "no-nyn" or "zh-guoyu" or "zh-hakka" or "zh-min" or "zh-min-nan" or "zh-xiang";
    }
}
