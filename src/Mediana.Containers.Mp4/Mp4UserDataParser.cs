// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text;

namespace Mediana.Containers.Mp4
{
    internal static class Mp4UserDataParser
    {
        private static readonly UTF8Encoding Utf8 = new(false, true);
        private static readonly UnicodeEncoding Utf16 = new(true, false, true);

        public static Mp4UserDataItem Read(Mp4Box box, string sourcePath, Mp4ParseBudget budget)
        {
            budget.TakeMetadataValue();
            ReadOnlySpan<byte> payload = box.Payload.Span;
            ushort? code = null;
            string? language = null;
            Encoding encoding = Utf8;
            ReadOnlySpan<byte> text;
            switch (box.Type)
            {
                case "name":
                    text = payload;
                    break;
                case "tnam":
                    if (Mp4Binary.UInt32(payload, 0) != 0)
                    {
                        throw new InvalidDataException("The QuickTime track name reserved field must be zero.");
                    }
                    code = Mp4Binary.UInt16(payload, 4);
                    language = Mp4LanguageParser.ReadPackedCode(code.Value);
                    text = payload[6..];
                    if (text.Length >= 2 && text[0] == 0xfe && text[1] == 0xff)
                    {
                        encoding = Utf16;
                        text = text[2..];
                        if (text.Length < 2 || (text.Length & 1) != 0 || text[^2] != 0 || text[^1] != 0)
                        {
                            throw new InvalidDataException("The QuickTime track name must be a terminated UTF-16BE string.");
                        }
                        text = text[..^2];
                        for (int index = 0; index < text.Length; index += 2)
                        {
                            if (text[index] == 0 && text[index + 1] == 0)
                            {
                                throw new InvalidDataException("The QuickTime track name contains an embedded terminator.");
                            }
                        }
                    }
                    else
                    {
                        if (text.IsEmpty || text[^1] != 0 || text[..^1].Contains((byte)0))
                        {
                            throw new InvalidDataException("The QuickTime track name must be one null-terminated UTF-8 string.");
                        }
                        text = text[..^1];
                    }
                    break;
                default:
                    throw new NotSupportedException($"QuickTime user-data type '{box.Type}' is not supported.");
            }
            budget.TakeMetadataText(text.Length);
            return new(box.Type, payload.Length, encoding.GetString(text), code, language, sourcePath);
        }
    }
}
