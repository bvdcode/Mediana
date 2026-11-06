// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using System.Text;
namespace Mediana.Tests
{
    internal static class PdfTextTestData
    {
        internal static byte[] Create(bool tagged = false, bool unicode = false, bool actualText = false, bool badSecond = false, string? cmap = null)
        {
            string content = unicode ? "BT /F1 12 Tf 1 0 0 1 20 150 Tm <0102> Tj ET" : actualText
                ? "/P <</MCID 0>> BDC BT /F1 12 Tf 1 0 0 1 20 150 Tm <FF> Tj ET EMC"
                : "/P <</MCID 0>> BDC BT /F1 12 Tf 1 0 0 1 20 50 Tm (Bottom) Tj ET EMC /P <</MCID 1>> BDC BT /F1 12 Tf 1 0 0 1 20 150 Tm (Top) Tj ET EMC";
            List<string> objects = ["<< /Type /Catalog /Pages 2 0 R"+(tagged ? " /StructTreeRoot 8 0 R" : "")+" >>", "<< /Type /Pages /Count 2 /Kids [3 0 R 6 0 R] >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Resources << /Font << /F1 4 0 R >> /XObject << /Unused 7 0 R >> >> /Contents 5 0 R >>",
                "<< /Type /Font /Subtype /Type1 /BaseFont /"+(actualText ? "Symbol" : "Helvetica")+(unicode ? " /ToUnicode 11 0 R" : " /Encoding /WinAnsiEncoding")+" >>",
                Stream(content),"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Resources << /Font << /F1 4 0 R >> >> /Contents 12 0 R >>",
                "<< /Type /XObject /Subtype /Image /Width 100000 /Height 100000 /Filter /Unsupported /Length 3 >>\nstream\nBAD\nendstream",
                "<< /Type /StructTreeRoot /K [9 0 R] >>","<< /Type /StructElem /S /Document /P 8 0 R /K [10 0 R] >>",
                "<< /Type /StructElem /S /P /P 9 0 R /Pg 3 0 R /K [0 1]"+(actualText ? " /ActualText <FEFF041F044004380432043504420020D83DDE00>" : "")+" >>",
                Stream(cmap ?? "/CIDInit /ProcSet findresource begin 12 dict begin begincmap 1 begincodespacerange <00> <FF> endcodespacerange 2 beginbfchar <01> <D83DDE00> <02> <00660069> endbfchar endcmap end end"),Stream(badSecond ? "BT /F1 12 Tf (unterminated" : "BT /F1 12 Tf 1 0 0 1 20 100 Tm (Second page) Tj ET")];
            return Build(objects);
        }
        internal static byte[] Build(IReadOnlyList<string> objects)
        {
            using MemoryStream buffer = new();
            Write("%PDF-1.7\n"); List<long> offsets = [0];
            for (int i = 0; i < objects.Count; i++) { offsets.Add(buffer.Position); Write((i + 1).ToString(CultureInfo.InvariantCulture) + " 0 obj\n" + objects[i] + "\nendobj\n"); }
            long xref = buffer.Position; Write("xref\n0 " + (objects.Count + 1).ToString(CultureInfo.InvariantCulture) + "\n0000000000 65535 f \n");
            foreach (long offset in offsets.Skip(1)) { Write(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n"); }
            Write("trailer\n<< /Size " + (objects.Count + 1).ToString(CultureInfo.InvariantCulture) + " /Root 1 0 R >>\nstartxref\n" + xref.ToString(CultureInfo.InvariantCulture) + "\n%%EOF\n"); return buffer.ToArray();
            void Write(string text) => buffer.Write(Encoding.Latin1.GetBytes(text));
        }
        internal static string Stream(string content) => "<< /Length " + Encoding.Latin1.GetByteCount(content).ToString(CultureInfo.InvariantCulture) + " >>\nstream\n" + content + "\nendstream";
    }
}
