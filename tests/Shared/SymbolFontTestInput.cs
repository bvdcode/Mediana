// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Documents.Pdf;
using Mediana.Documents.Word;
using Mediana.Drawing;
using Mediana.Drawing.Fonts;
using Mediana.Images.Svg;

namespace Mediana.Tests
{
    internal static class SymbolFontTestInput
    {
        private const string Symbols = "Noto Sans Symbols 2";

        internal static void VerifyResolution()
        {
            FontRequest request = new("Canada1500");
            FontProgram primary = BundledFontPrograms.GetProgram(request.Family);
            FontProgram symbols = BundledFontPrograms.GetProgram(Symbols);
            TextOptions options = new() { SubstituteMissingFonts = true };
            TextRenderer text = new(options);
            Require(ReferenceEquals(text.ResolveGlyph(request, 'A').Font.SharedProgram, primary));
            foreach (int scalar in new[] { 0x25CF, 0x25AA })
            {
                (TrueTypeFont font, int glyph) = text.ResolveGlyph(request, scalar);
                Require(glyph != 0 && text.Outline(font, glyph).CommandCount > 0);
                Require(ReferenceEquals(font.SharedProgram, symbols));
                Require(ReferenceEquals(font, text.ResolveGlyph(request, scalar).Font));
                Require(ReferenceEquals(new TextRenderer(options).ResolveGlyph(request, scalar).Font.SharedProgram, symbols));
                foreach ((bool bold, bool italic) in new[] { (false, false), (true, false), (false, true), (true, true) })
                { Require(ReferenceEquals(symbols, BundledFontPrograms.GetProgram(Symbols, bold, italic))); }
                Require(new TextRenderer().ResolveGlyph(request, scalar).Glyph == 0);
                Require(new TextRenderer(options with { FallbackFamilies = [] }).ResolveGlyph(request, scalar).Glyph == 0);
            }
            TextRenderer limited = new(options with { MaximumFontBytes = primary.Program.Length });
            Require(limited.ResolveGlyph(request, 'A').Glyph != 0);
            ExpectLimit(() => limited.ResolveGlyph(request, 0x25CF));
            ExpectLimit(() => new TextRenderer(options with { MaximumFontFaces = 1 }).ResolveGlyph(request, 0x25AA));
            FontProgram custom = new(BundledFontPrograms.GetFont(Symbols));
            TextRenderer provided = new(options with { FallbackFontProgramResolver = (_, _) => custom });
            Require(ReferenceEquals(custom, provided.ResolveGlyph(request, 0x25CF).Font.SharedProgram));
        }

        internal static async Task VerifyDocumentsAsync()
        {
            TextOptions text = await TextResources.LoadAsync([], new() { SubstituteMissingFonts = true });
            VerifySvg(text);
            await VerifyWordAsync(text);
            await VerifyPdfAsync(text);
        }

        private static void VerifySvg(TextOptions text)
        {
            const string start = "<svg xmlns='http://www.w3.org/2000/svg' width='120' height='60'><text x='4' y='44' font-family='Canada1500' font-size='24'>";
            const string end = "</text></svg>";
            using SvgDocument actual = SvgReader.Read(start + "A●▪A" + end, new() { Text = text });
            using SvgDocument reference = SvgReader.Read(start + "A<tspan font-family='Noto Sans Symbols 2'>●▪</tspan>A" + end,
                new() { Text = new() });
            Require(actual.UnsupportedFeatures.Count == 0);
            using PixelBuffer expected = SvgRenderer.Render(reference, new() { Supersampling = 1 });
            MediaBufferAllocator allocator = new(120 * 60 * 4);
            using (PixelBuffer image = SvgRenderer.Render(actual, new() { Supersampling = 1 }, allocator: allocator))
            { Require(image.Pixels.Span.SequenceEqual(expected.Pixels.Span)); }
            Require(allocator.AllocatedBytes == 0);
        }

        private static async Task VerifyWordAsync(TextOptions text)
        {
            WordDocument actual = WordReader.Read(WordTestInput.Create("<w:p><w:r><w:t>A●▪A</w:t></w:r></w:p>",
                WordRenderTestInput.Fonts, page: WordRenderTestInput.SmallPage));
            WordDocument reference = WordReader.Read(WordTestInput.Create("<w:p><w:r><w:t>A</w:t></w:r><w:r><w:rPr><w:rFonts w:ascii='Noto Sans Symbols 2'/></w:rPr><w:t>●▪</w:t></w:r><w:r><w:t>A</w:t></w:r></w:p>",
                WordRenderTestInput.Fonts, page: WordRenderTestInput.SmallPage));
            WordLayout layout = WordRenderer.Layout(actual, new() { Text = text });
            Require(layout.PageCount == 1);
            Require(layout.Warnings.Any(warning => warning.Contains(Symbols, StringComparison.Ordinal)));
            Require(!layout.Warnings.Any(warning => warning.Contains("missing-glyph", StringComparison.Ordinal)));
            using PixelBuffer image = await WordRenderer.RenderAsync(layout, 1, new() { Dpi = 72, Supersampling = 1 });
            using PixelBuffer expected = await WordRenderer.RenderAsync(WordRenderer.Layout(reference), 1, new() { Dpi = 72, Supersampling = 1 });
            Require(image.Pixels.Span.SequenceEqual(expected.Pixels.Span));
        }

        private static async Task VerifyPdfAsync(TextOptions text)
        {
            const string start = "BT /F 20 Tf 10 40 Td ";
            using PdfDocument actual = await PdfReader.ReadAsync(new MemoryStream(Pdf(start + "<000100020003> Tj ET")), new() { Text = text });
            using PdfDocument reference = await PdfReader.ReadAsync(new MemoryStream(Pdf("BT /S 20 Tf 10 40 Td <00010002> Tj /F 20 Tf <0003> Tj ET")),
                new() { Text = new() { FontProgramResolver = request => BundledFontPrograms.GetProgram(request.Family == "Symbols" ? Symbols : "Canada1500") } });
            using PdfPage page = actual.GetPage(1);
            Require(page.UnsupportedFeatures.Count == 0);
            Require(page.Warnings.Any(warning => warning.Contains(Symbols, StringComparison.Ordinal)));
            actual.Dispose();
            using PixelBuffer image = PdfRenderer.Render(page, new() { Dpi = 72, Supersampling = 1 });
            using PixelBuffer expected = PdfRenderer.Render(reference, 1, new() { Dpi = 72, Supersampling = 1 });
            Require(image.Pixels.Span.SequenceEqual(expected.Pixels.Span));
            PdfReaderOptions limited = new() { Text = text, MaximumDecodedBytes = BundledFontPrograms.GetProgram("Canada1500").Program.Length + 1024 };
            using PdfDocument bounded = await PdfReader.ReadAsync(new MemoryStream(Pdf(start + "<0001> Tj ET")), limited);
            ExpectLimit(() => { using PixelBuffer ignored = PdfRenderer.Render(bounded, 1); });
        }

        private static byte[] Pdf(string content) => PdfTestDocument.Create(content, resources: "/Font << /F 5 0 R /S 8 0 R >>", extraObjects:
        [PdfTestDocument.Bytes("<< /Type /Font /Subtype /Type0 /BaseFont /Canada1500 /Encoding /Identity-H /DescendantFonts [6 0 R] /ToUnicode 7 0 R >>"),
         PdfTestDocument.Bytes("<< /Type /Font /Subtype /CIDFontType2 /BaseFont /Canada1500 /DW 1000 >>"),
         PdfTestDocument.Stream(PdfTestDocument.Bytes("begincmap 1 begincodespacerange <0000> <FFFF> endcodespacerange 3 beginbfchar <0001> <25CF> <0002> <25AA> <0003> <0041> endbfchar endcmap")),
         PdfTestDocument.Bytes("<< /Type /Font /Subtype /Type0 /BaseFont /Symbols /Encoding /Identity-H /DescendantFonts [9 0 R] /ToUnicode 7 0 R >>"),
         PdfTestDocument.Bytes("<< /Type /Font /Subtype /CIDFontType2 /BaseFont /Symbols /DW 1000 >>")]);

        private static void ExpectLimit(Action action)
        {
            try { action(); }
            catch (InvalidDataException) { return; }
            throw new InvalidOperationException("Symbol font resource limit was not enforced.");
        }

        private static void Require(bool condition)
        {
            if (!condition) { throw new InvalidOperationException("Symbol font rendering assertion failed."); }
        }
    }
}
