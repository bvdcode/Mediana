// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Documents;
using Mediana.Documents.Pdf;
using Mediana.Documents.Presentation;
using Mediana.Documents.Spreadsheets;
using Mediana.Documents.Word;
using Mediana.Documents.Xls;
using Mediana.Documents.Xlsx;
using Mediana.Tests;
using Xunit;
namespace Mediana.Documents.Tests
{
    public class DocumentReaderTests
    {
        [Fact] public Task PublicReadersPreserveValuesStructureAndPixels() => DocumentReadersTestInput.VerifyAsync();
        [Theory]
        [InlineData("/", true)]
        [InlineData(" vertOverflow='clip'/", false)]
        [InlineData("><a:normAutofit/></a:bodyPr", false)]
        public Task PresentationTextHonorsAutomaticSizingAndVerticalOverflow(string properties, bool overflows)
            => DocumentReadersTestInput.VerifyPresentationTextBoundsAsync(properties, overflows);
        [Theory]
        [InlineData(" wrap='none'/", true)]
        [InlineData(" wrap='none' horzOverflow='clip'/", false)]
        [InlineData(" wrap='none'><a:normAutofit fontScale='100%' lnSpcReduction='0%'/></a:bodyPr", false)]
        public Task PresentationTextHonorsHorizontalOverflow(string properties, bool overflows)
            => DocumentReadersTestInput.VerifyPresentationTextBoundsAsync(properties, overflows, horizontal: true);
        [Theory]
        [InlineData(0, "1899-12-31")]
        [InlineData(1, "1900-01-01")]
        [InlineData(59, "1900-02-28")]
        [InlineData(61, "1900-03-01")]
        [InlineData(-1, "1899-12-30")]
        public void ExcelDatesRetainTheOriginalSerial(double serial, string date)
        {
            SpreadsheetDateTime value = new(serial, SpreadsheetDateSystem.Excel1900);
            Assert.Equal(serial, value.SerialValue); Assert.Equal(date, value.Value?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        }
        [Theory]
        [InlineData(60)]
        [InlineData(60.5)]
        public void FictitiousExcelLeapDayIsNotInvented(double serial) => Assert.Null(new SpreadsheetDateTime(serial, SpreadsheetDateSystem.Excel1900).Value);
        [Theory]
        [InlineData(14, null, true)]
        [InlineData(0, null, false)]
        [InlineData(164, "0.00 \"days\"", false)]
        [InlineData(164, "yyyy-mm-dd", true)]
        [InlineData(164, "[h]:mm", true)]
        [InlineData(164, "0.0E+00", false)]
        public void DateFormatDetectionRespectsLiterals(int id, string? code, bool date) => Assert.Equal(date, SpreadsheetNumberFormats.IsDateTime(id, code));
        [Fact] public void Excel1904IsIndependent() => Assert.Equal(new DateTime(1904, 1, 1), new SpreadsheetDateTime(0, SpreadsheetDateSystem.Excel1904).Value);
        [Fact] public void WrongTypedValueAccessFails() => Assert.Throws<InvalidOperationException>(() => SpreadsheetValue.FromText("1").Number);
        [Theory]
        [InlineData("<row r='1'><c r='B1'/><c r='A1'/></row>")]
        [InlineData("<row r='2'><c r='A1'/></row>")]
        [InlineData("<row r='1'><c r='XFE1'/></row>")]
        [InlineData("<row r='1'><c t='b'><v>2</v></c></row>")]
        [InlineData("<row r='1'><c t='n'><v>NaN</v></c></row>")]
        [InlineData("<row r='1'><c t='s'><v>100</v></c></row>")]
        [InlineData("<row r='1'><c t='unknown'/></row>")]
        [InlineData("<row r='1'><c s='3'><v>1</v></c></row>")]
        public void InvalidXlsxCellsAreRejected(string rows)
        { using XlsxWorkbook workbook = XlsxReader.Open(XlsxWorkbookTestData.Create(rows)); Assert.ThrowsAny<Exception>(() => workbook.ReadRows(0).ToArray()); }
        [Fact]
        public void XlsxFirstRowDoesNotReadLaterMalformedRow()
        {
            using XlsxWorkbook workbook = XlsxReader.Open(XlsxWorkbookTestData.Create("<row r='1'><c><v>1</v></c></row><row r='2'><c t='n'><v>bad</v></c></row>"));
            Assert.Equal(1, workbook.ReadRows(0).First().Cells[0].Value.Number);
            Assert.Throws<InvalidDataException>(() => workbook.ReadRows(0).ToArray());
        }
        [Fact]
        public void CancellationStopsAllDocumentFormats()
        {
            using CancellationTokenSource source = new(); source.Cancel();
            Assert.Throws<OperationCanceledException>(() => XlsxReader.Open(XlsxWorkbookTestData.Create(), cancellationToken: source.Token));
            Assert.Throws<OperationCanceledException>(() => XlsReader.Open(XlsWorkbookTestData.Create(), cancellationToken: source.Token));
            Assert.Throws<OperationCanceledException>(() => PresentationReader.Open(PresentationTestData.Create(), cancellationToken: source.Token));
            Assert.Throws<OperationCanceledException>(() => WordTextReader.Read(WordTestInput.Create(""), cancellationToken: source.Token).First());
            using PdfDocument pdf = PdfReader.Open(PdfTextTestData.Create());
            Assert.Throws<OperationCanceledException>(() => pdf.ReadText(cancellationToken: source.Token).First());
        }
        [Fact]
        public void TextLimitsRejectRatherThanTruncate()
        {
            using XlsxWorkbook workbook = XlsxReader.Open(XlsxWorkbookTestData.Create());
            Assert.Throws<InvalidDataException>(() => workbook.ReadText(new() { MaximumCharacters = 2 }).ToArray());
            using PdfDocument pdf = PdfReader.Open(PdfTextTestData.Create());
            Assert.Throws<InvalidDataException>(() => pdf.ReadText(new() { MaximumBlocks = 1 }).ToArray());
        }
        [Fact]
        public async Task FailedPresentationRenderingReleasesBuffers()
        {
            using PresentationDocument document = PresentationReader.Open(PresentationTestData.Create());
            MediaBufferAllocator allocator = new(4);
            await Assert.ThrowsAsync<MediaMemoryLimitException>(async () => await PresentationRenderer.RenderAsync(document, 0, allocator: allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }
        [Theory]
        [InlineData("FF0000", "tint", 10000, 255, 230, 230)]
        [InlineData("0000FF", "hueMod", 50000, 0, 255, 0)]
        [InlineData("FF0000", "satMod", 50000, 191, 64, 64)]
        [InlineData("FF0000", "lumMod", 50000, 128, 0, 0)]
        [InlineData("FF0000", "shade", 50000, 128, 0, 0)]
        public async Task PresentationColorTransformsMatchDrawingMl(string color, string transform, int value, int r, int g, int b)
        {
            using PresentationDocument document = PresentationReader.Open(PresentationTestData.Create(shapeColor: color, colorTransform: "<a:" + transform + " val='" + value + "'/>"));
            using Drawing.PixelBuffer pixels = await PresentationRenderer.RenderAsync(document, 0, new() { Dpi = 72, Supersampling = 1, Text = new() { SubstituteMissingFonts = true } });
            Assert.Equal(new Drawing.Rgba32((byte)r, (byte)g, (byte)b), pixels[5, 5]);
        }
        [Fact]
        public void PdfCmapBlocksUseTheConfiguredOperationBudget()
        {
            string entries = string.Concat(Enumerable.Range(0, 128).Select(code => "<" + code.ToString("X2") + "> <" + (code + 65).ToString("X4") + "> "));
            string cmap = "begincmap 1 begincodespacerange <00> <FF> endcodespacerange 128 beginbfchar " + entries + "endbfchar endcmap";
            using PdfDocument pdf = PdfReader.Open(PdfTextTestData.Create(unicode: true, cmap: cmap));
            Assert.Equal("BC", pdf.ReadText().First().Text);
        }
        [Fact]
        public void UnusedSurrogateSlotsDoNotInvalidateMappedPdfText()
        {
            const string cmap = "begincmap 1 begincodespacerange <00> <FF> endcodespacerange 3 beginbfchar <01> <0041> <02> <0042> <03> <D800> endbfchar endcmap";
            using PdfDocument pdf = PdfReader.Open(PdfTextTestData.Create(unicode: true, cmap: cmap));
            Assert.Equal("AB", pdf.ReadText().First().Text);
        }
        [Fact]
        public void UsedUnpairedSurrogatesAreNotPresentedAsUnicode()
        {
            const string cmap = "begincmap 1 begincodespacerange <00> <FF> endcodespacerange 2 beginbfchar <01> <D800> <02> <0042> endbfchar endcmap";
            using PdfDocument pdf = PdfReader.Open(PdfTextTestData.Create(unicode: true, cmap: cmap));
            Assert.Throws<NotSupportedException>(() => pdf.ReadText().First());
        }
        [Fact]
        public void WordTextRetainsSparseTableCellPaths()
        {
            const string body = "<w:tbl><w:tr><w:trPr><w:gridBefore w:val='1'/></w:trPr><w:tc><w:tcPr><w:gridSpan w:val='2'/></w:tcPr><w:p><w:r><w:t>A</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>B</w:t></w:r></w:p></w:tc></w:tr></w:tbl>";
            DocumentTextBlock[] blocks = WordTextReader.Read(WordTestInput.Create(body)).ToArray();
            Assert.Equal(new[] { 1, 3 }, blocks.Select(block => block.Path.Single(segment => segment.Kind == DocumentTextNodeKind.Cell).Index));
        }
        [Fact]
        public void CfbCyclesAreRejected()
        {
            byte[] input = XlsWorkbookTestData.Create(mini: false); XlsWorkbookTestData.U32(input, 512 + 2 * 4, 2);
            Assert.Throws<InvalidDataException>(() => XlsReader.Open(input));
        }
        [Fact]
        public void CfbTruncationIsRejected()
        { byte[] input = XlsWorkbookTestData.Create(); Assert.Throws<InvalidDataException>(() => XlsReader.Open(input.AsMemory(0, input.Length - 1))); }
        [Fact]
        public void DisposedWorkbooksCannotReadRows()
        {
            XlsxWorkbook xlsx = XlsxReader.Open(XlsxWorkbookTestData.Create()); xlsx.Dispose(); Assert.Throws<ObjectDisposedException>(() => xlsx.ReadRows(0).First());
            XlsWorkbook xls = XlsReader.Open(XlsWorkbookTestData.Create()); xls.Dispose(); Assert.Throws<ObjectDisposedException>(() => xls.ReadRows(0).First());
        }
    }
}
