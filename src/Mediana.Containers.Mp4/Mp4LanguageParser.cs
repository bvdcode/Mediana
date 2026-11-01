// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text;
using Mediana.Formats.Metadata;

namespace Mediana.Containers.Mp4
{
    internal static class Mp4LanguageParser
    {
        public static Mp4LanguageInfo Parse(ReadOnlySpan<byte> header, Mp4Box? extended, bool quickTime, Mp4ParseBudget budget)
        {
            ushort code = Mp4Binary.UInt16(header, header[0] == 0 ? 20 : 32);
            if (extended is Mp4Box box)
            {
                Mp4Binary.FullBox(box.Payload.Span);
                ReadOnlySpan<byte> text = box.Payload.Span[4..];
                if (text.IsEmpty || text[^1] != 0 || text[..^1].Contains((byte)0))
                {
                    throw new InvalidDataException("The MP4 extended language tag must be one null-terminated string.");
                }
                budget.TakeMetadataText(text.Length - 1);
                string value = Encoding.Latin1.GetString(text[..^1]);
                string? language = null;
                if (Bcp47LanguageTag.IsWellFormed(value))
                {
                    language = value;
                }
                return new(code, value, language);
            }
            return new(code, null, ReadCode(code, quickTime));
        }

        private static string? ReadCode(ushort code, bool quickTime)
        {
            if (code == 0x7fff)
            {
                return null;
            }
            if (code < 0x400)
            {
                if (quickTime)
                {
                    return QuickTimeLanguageCodes.GetLanguage(code);
                }
                return null;
            }
            return ReadPackedCode(code);
        }

        public static string? ReadPackedCode(ushort code)
        {
            if (code == 0x7fff || (code & 0x8000) != 0)
            {
                return null;
            }
            Span<char> characters = stackalloc char[3];
            for (int index = 0; index < 3; index++)
            {
                int value = (code >> (10 - index * 5)) & 31;
                if (value is < 1 or > 26)
                {
                    return null;
                }
                characters[index] = (char)(value + 0x60);
            }
            return new string(characters);
        }
    }
}
