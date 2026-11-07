// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Memory;
using Mediana.Documents;
using Mediana.Documents.Pdf;
using Mediana.Documents.OpenXml;
using Mediana.Core.Video;
using Mediana.Documents.Presentation;
using Mediana.Documents.Spreadsheets;
using Mediana.Documents.Word;
using Mediana.Documents.Xls;
using Mediana.Documents.Xlsx;
using Mediana.Drawing;
namespace Mediana.Tests
{
    internal static class DocumentReadersTestInput
    {
        internal static async Task VerifyAsync()
        {
            await VerifyPresentationTextBoundsAsync("/", true);
            await VerifyPresentationTextBoundsAsync(" vertOverflow='clip'/", false);
            await VerifyPresentationTextBoundsAsync("><a:normAutofit/></a:bodyPr", false);
            await VerifyPresentationTextBoundsAsync(" wrap='none' horzOverflow='clip'/", false, horizontal: true);
            await VerifyPresentationTextBoundsAsync(" wrap='none'><a:normAutofit fontScale='100%' lnSpcReduction='0%'/></a:bodyPr", false, horizontal: true);
            foreach (bool strict in new[] { false, true })
            {
                using XlsxWorkbook xlsx = XlsxReader.Open(XlsxWorkbookTestData.Create(badSecond: true, strict: strict));
                SpreadsheetRow[] rows = xlsx.ReadRows(0).ToArray();
                Check(rows.Length == 2 && rows[0].RowIndex == 1 && rows[1].RowIndex == 4, "XLSX sparse rows");
                Check(rows[0].Cells[0].Value.Text == "Привет\t世界_x0041_", "XLSX Unicode and escapes");
                Check(rows[0].Cells[1].Value.Boolean && rows[0].Cells[2].Value.Error == "#REF!", "XLSX typed values");
                Check(rows[0].Cells[3].Value.DateTime.SerialValue == 60.5 && rows[0].Cells[3].Value.DateTime.Value is null, "XLSX fictitious leap day");
                Check(rows[0].Cells[4].Formula?.Text == "2+2" && rows[0].Cells[4].Value.Number == 4, "XLSX cached formula");
                Check(rows[0].Cells[5].Formula is { HasCachedValue: false, Kind: SpreadsheetFormulaKind.Shared, SharedIndex: 3 }, "XLSX missing formula cache");
                Check(rows[1].Cells[0].Value.Text == "Unicode ● ▪ 😀" && rows[1].Cells[1].Value.Text == "", "XLSX inline and empty text");
                Check(xlsx.ReadText(new() { PartCount = 1 }).Count() == 10, "XLSX bounded text selection");
                using PresentationDocument deck = PresentationReader.Open(PresentationTestData.Create(badSecond: true, strict: strict));
                DocumentTextBlock text = deck.ReadText().First();
                Check(text.Text == "Привет ● ▪" && text.PartIndex == 0 && text.Structure == DocumentTextStructure.Declared, "PPTX selected text");
                MediaBufferAllocator allocator = new(4 * 1024 * 1024);
                using (PixelBuffer pixels = await PresentationRenderer.RenderAsync(deck, 0, new() { Dpi = 72, Supersampling = 1, Text = new() { SubstituteMissingFonts = true } }, allocator: allocator))
                {
                    Check(pixels.Width == 144 && pixels.Height == 108, "PPTX page geometry");
                    Check(pixels[5, 5] == new Rgba32(255, 0, 0) && pixels[5, 100] == new Rgba32(0, 0, 255) && pixels[138, 100] == new Rgba32(0, 255, 0), "PPTX slide/layout/master pixels");
                    int dark = 0;
                    for (int y = 20; y < 65; y++)
                    { for (int x = 20; x < 120; x++) { if (pixels[x, y] is { R: < 64, G: < 64, B: < 64 }) { dark++; } } }
                    Check(dark > 20, "PPTX text outlines in the inherited placeholder");
                }
                Check(allocator.AllocatedBytes == 0, "PPTX allocation release");
            }
            foreach ((bool mini, bool v4) in new[] { (true, false), (false, false), (false, true) })
            {
                using XlsWorkbook xls = XlsReader.Open(XlsWorkbookTestData.Create(mini, v4));
                SpreadsheetRow[] rows = xls.ReadRows(0).ToArray();
                Check(rows.Length == 2 && rows[0].Cells.Count == 6, "XLS CFB/BIFF rows");
                Check(rows[0].Cells[0].Value.Text == "Привет 世界 😀" && rows[0].Cells[1].Value.Boolean, "XLS Unicode and Boolean");
                Check(rows[0].Cells[2].Value.Error == "#REF!" && rows[0].Cells[3].Value.DateTime.Value is null, "XLS errors and leap day");
                Check(rows[0].Cells[4].Value.Number == 4 && rows[0].Cells[4].Formula?.BinaryTokens.Span.SequenceEqual(new byte[] { 0x1e, 4, 0 }) == true, "XLS formula token/cache ownership");
                Check(rows[0].Cells[5].Value.Text == "cached 😀" && Math.Abs(rows[1].Cells[0].Value.Number - 123.45) < 1e-10, "XLS string formula and RK");
                Check(xls.ReadText().Count() == 7, "XLS shared document text");
            }
            using (XlsWorkbook continued = XlsReader.Open(XlsWorkbookTestData.Create(continued: true)))
            { Check(continued.ReadRows(0).First().Cells[0].Value.Text == "abcЖЕ一", "BIFF compressed/wide CONTINUE"); }
            byte[] word = WordTestInput.Create("<w:del><w:p><w:r><w:t>Deleted</w:t></w:r></w:p></w:del><w:p><w:r><w:t>Unicode 😀</w:t><w:tab/><w:t>● ▪</w:t><w:br/><w:t>Second line</w:t></w:r></w:p>", page: null);
            Check(WordTextReader.Read(word).Single().Text == "Unicode 😀\t● ▪\nSecond line", "DOCX text without layout metadata");
            PdfReaderOptions noRendering = new() { Text = new() { FontResolver = _ => throw new InvalidOperationException("Font resolution is not part of text extraction.") }, ImageAllocator = new(4) };
            using (PdfDocument pdf = PdfReader.Open(PdfTextTestData.Create(badSecond: true), noRendering))
            {
                DocumentTextBlock[] blocks = pdf.ReadText(new() { PartCount = 1 }).ToArray();
                Check(blocks.Length == 2 && blocks[0].Text == "Top" && blocks[1].Text == "Bottom" && blocks.All(item => item.Structure == DocumentTextStructure.Reconstructed), "PDF reconstructed order");
                Check(noRendering.ImageAllocator.AllocatedBytes == 0, "PDF text without raster allocations");
            }
            using (PdfDocument pdf = PdfReader.Open(PdfTextTestData.Create(tagged: true)))
            {
                DocumentTextBlock[] declared = pdf.ReadText(new() { PartCount = 1 }).ToArray();
                Check(declared.Length == 2 && declared[0].Text == "Bottom" && declared[1].Text == "Top" && declared.All(item => item.Structure == DocumentTextStructure.Declared), "PDF declared tag order");
            }
            using (PdfDocument pdf = PdfReader.Open(PdfTextTestData.Create(unicode: true)))
            { Check(pdf.ReadText().First().Text == "😀fi", "PDF surrogate and multi-scalar ToUnicode"); }
            using (PdfDocument pdf = PdfReader.Open(PdfTextTestData.Create(tagged: true, actualText: true)))
            { Check(pdf.ReadText().First().Text == "Привет 😀", "PDF ActualText without glyph decoding"); }
            OpenXmlImageDecoder adapter = new([new PresentationPageDecoder(renderOptions: new() { Dpi = 72, Supersampling = 1 })]);
            byte[] encodedPresentation = PresentationTestData.Create();
            Mediana.Core.Metadata.ImageInfo info = await adapter.ReadInfoAsync(encodedPresentation);
            Check(info.FormatId == "pptx" && info.PageCount == 2 && info.Width == 144 && info.Height == 108, "PPTX page metadata registration");
            MediaBufferAllocator frameBudget = new(144 * 108 * 4);
            using (VideoFrame frame = await adapter.DecodeAsync(encodedPresentation, new() { PageIndex = 1 }, frameBudget))
            {
                Check(frame.GetRow(0, 0).SequenceEqual(Enumerable.Range(0, 144).SelectMany(_ => new byte[] { 0, 0, 255, 255 }).ToArray()), "PPTX selected page pixels");
                Check(frameBudget.AllocatedBytes == 144 * 108 * 4, "PPTX published frame retains a single pixel buffer");
            }
            Check(frameBudget.AllocatedBytes == 0, "PPTX published frame release");
            int decoded = 0;
            using (PresentationDocument deck = PresentationReader.Open(PresentationTestData.Create(picture: true)))
            {
                PresentationRenderOptions options = new() { Dpi = 72, Supersampling = 1, Text = new() { SubstituteMissingFonts = true }, Images = new([new EmbeddedTestImageDecoder(decoded: () => decoded++)]) };
                using PixelBuffer pixels = await PresentationRenderer.RenderAsync(deck, 0, options);
                Check(decoded == 1 && pixels[125, 65] == new Rgba32(21, 43, 65), "PPTX shared embedded image registry");
            }
        }
        internal static async Task VerifyPresentationTextBoundsAsync(string properties, bool overflows, bool horizontal = false)
        {
            string text = horizontal ? "MMMMMMMMMMMMMMMM" : string.Join(" ", Enumerable.Repeat("MMMM", 32));
            using PresentationDocument document = PresentationReader.Open(PresentationTestData.Create(titleText: text, titleBodyProperties: properties), new() { MaximumGlyphs = text.Length });
            using PixelBuffer pixels = await PresentationRenderer.RenderAsync(document, 0, new() { Dpi = 72, Supersampling = 1, Text = new() { SubstituteMissingFonts = true } });
            int outside = 0, inside = 0;
            for (int y = 20; y < (horizontal ? 65 : 96); y++)
            {
                for (int x = 20; x < (horizontal ? 144 : 120); x++)
                {
                    Rgba32 color = pixels[x, y];
                    if (color.R >= 80 || color.G >= 80 || color.B >= 80) { continue; }
                    if (horizontal ? x >= 120 : y >= 65) { outside++; } else { inside++; }
                }
            }
            Check(inside > 0 && overflows == (outside > 0), "PPTX automatic text sizing and independent overflow axes");
        }
        private static void Check(bool condition, string feature) { if (!condition) { throw new InvalidDataException("Document reader mismatch: " + feature); } }
    }
}
