// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using System.Text;
using Mediana.Documents.Pdf;
using Mediana.Drawing;
using Xunit;
namespace Mediana.Tests
{
    public class PdfStructureTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task NewestCrossReferenceEntryWinsAcrossIncrementalUpdates(bool freed)
        {
            byte[] initial = PdfTestDocument.Create("1 0 0 rg 10 10 20 20 re f");
            string text = Encoding.Latin1.GetString(initial);
            string previous = text[(text.LastIndexOf("startxref", StringComparison.Ordinal) + 9)..].Trim().Split('\n')[0];
            using MemoryStream output = new(); output.Write(initial);
            long replacement = output.Position;
            output.Write(PdfTestDocument.Bytes("4 0 obj\n")); output.Write(PdfTestDocument.Stream(PdfTestDocument.Bytes("0 0 1 rg 10 10 20 20 re f"))); output.Write(PdfTestDocument.Bytes("\nendobj\n"));
            long xref = output.Position;
            string entry = freed ? "0000000000 00001 f" : replacement.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n";
            output.Write(PdfTestDocument.Bytes($"xref\n4 1\n{entry} \ntrailer\n<< /Size 5 /Root 1 0 R /Prev {previous} >>\nstartxref\n{xref}\n%%EOF\n"));
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(output.ToArray()));
            if (freed) { Assert.Throws<InvalidDataException>(() => document.GetPage(1)); }
            else { Assert.Equal(new Rgba32(0, 0, 255), PdfRenderer.Render(document.GetPage(1), new() { Dpi = 72, Supersampling = 1 })[15, 85]); }
        }
        [Fact]
        public async Task CyclicPageTreesAndExcessiveDecodedStreamsAreRejected()
        {
            byte[] initial = PdfTestDocument.Create("0 0 m 1 1 l S");
            byte[] cycle = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(initial).Replace("/Kids [3 0 R]", "/Kids [2 0 R]", StringComparison.Ordinal));
            await Assert.ThrowsAsync<InvalidDataException>(() => PdfReader.ReadAsync(new MemoryStream(cycle)));
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(initial), new() { MaximumDecodedBytes = 2 });
            Assert.Throws<InvalidDataException>(() => document.GetPage(1));
        }
        [Fact]
        public async Task MissingFontOutlinesAreReportedOnlyForPaintedText()
        {
            byte[] font = PdfTestDocument.Bytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
            byte[] initial = PdfTestDocument.Create("BT /F 12 Tf ET 1 0 0 rg 0 0 10 10 re f", resources: "/Font << /F 5 0 R >>", extraObjects: font);
            using PdfDocument unused = await PdfReader.ReadAsync(new MemoryStream(initial));
            Assert.Empty(unused.GetPage(1).UnsupportedFeatures);
            byte[] text = PdfTestDocument.Create("BT /F 12 Tf (Actual text) Tj ET", resources: "/Font << /F 5 0 R >>", extraObjects: font);
            using PdfDocument painted = await PdfReader.ReadAsync(new MemoryStream(text));
            Assert.Throws<NotSupportedException>(() => PdfRenderer.Render(painted.GetPage(1)));
        }
    }
}
