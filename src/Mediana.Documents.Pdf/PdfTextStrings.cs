// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text;
namespace Mediana.Documents.Pdf
{
    internal static class PdfTextStrings
    {
        internal static string Decode(PdfValue value)
        {
            if (value.Kind != PdfValueKind.String) { throw new InvalidDataException("Expected a PDF text string."); }
            ReadOnlySpan<byte> bytes = value.Bytes;
            if (bytes.StartsWith(new byte[] { 0xfe, 0xff })) { return new UnicodeEncoding(true, false, true).GetString(bytes[2..]); }
            if (bytes.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) { return new UTF8Encoding(false, true).GetString(bytes[3..]); }
            const string low = "˘ˇˆ˙˝˛˚˜";
            const string high = "•†‡…—–ƒ⁄‹›−‰„“”‘’‚™ﬁﬂŁŒŠŸŽıłœšž";
            StringBuilder text = new(bytes.Length);
            foreach (byte b in bytes)
            {
                int code = b switch { >= 0x18 and <= 0x1f => low[b - 0x18], >= 0x80 and <= 0x9e => high[b - 0x80], 0xa0 => 0x20ac, 0x7f or 0x9f or 0xad => throw new InvalidDataException("Undefined PDFDocEncoding character."), _ => b };
                text.Append(char.ConvertFromUtf32(code));
            }
            return text.ToString();
        }
    }
}
