// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.Xlsx;
namespace Mediana.Tests
{
    internal static class XlsxWorkbookTestData
    {
        internal const string Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        internal static byte[] Create(string? rows = null, bool badSecond = false, bool strict = false)
        {
            string ns = strict ? "http://purl.oclc.org/ooxml/spreadsheetml/main" : Ns;
            string office = strict ? "http://purl.oclc.org/ooxml/officeDocument/relationships/" : DocumentPackageTestData.Office;
            return DocumentPackageTestData.Create(XlsxReader.WorkbookContentType, "xl/book.xml", new()
            {
                ["xl/book.xml"] = "<workbook xmlns='" + ns + "' xmlns:r='" + office.TrimEnd('/') + "'><workbookPr date1904='0'/><sheets><sheet name='Лист 😀' sheetId='7' r:id='one'/><sheet name='Second' state='hidden' sheetId='9' r:id='two'/></sheets></workbook>",
                ["xl/_rels/book.xml.rels"] = DocumentPackageTestData.Relations(("one", "worksheet", "sheets/one.xml"), ("two", "worksheet", "sheets/two.xml"), ("strings", "sharedStrings", "strings.xml"), ("styles", "styles", "styles.xml")).Replace(DocumentPackageTestData.Office, office, StringComparison.Ordinal),
                ["xl/sheets/one.xml"] = "<worksheet xmlns='" + ns + "'><sheetData>" + (rows ?? "<row r='2'><c r='A2' t='s'><v>0</v></c><c r='C2' t='b'><v>1</v></c><c r='D2' t='e'><v>#REF!</v></c><c r='E2' s='1'><v>60.5</v></c><c r='F2'><f>2+2</f><v>4</v></c><c r='G2'><f t='shared' si='3'/></c><c r='H2' t='d'><v>2026-11-06T12:30:00Z</v></c></row><row r='5'><c r='B5' t='inlineStr'><is><r><t>Unicode </t></r><r><t>● ▪ 😀</t></r><rPh><t>ignored</t></rPh></is></c><c r='C5' t='str'><f>TEXT()</f><v></v></c><c r='D5'/></row>") + "</sheetData></worksheet>",
                ["xl/sheets/two.xml"] = badSecond ? "<broken" : "<worksheet xmlns='" + ns + "'><sheetData/></worksheet>",
                ["xl/strings.xml"] = "<sst xmlns='" + ns + "' count='1' uniqueCount='1'><si><t>Привет_x0009_世界_x005F_x0041_</t></si></sst>",
                ["xl/styles.xml"] = "<styleSheet xmlns='" + ns + "'><cellXfs count='2'><xf numFmtId='0'/><xf numFmtId='14'/></cellXfs></styleSheet>"
            });
        }
    }
}
