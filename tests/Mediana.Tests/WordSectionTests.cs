// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Mediana.Documents.Word;
using Mediana.Drawing;

namespace Mediana.Tests
{
    public class WordSectionTests
    {
        [Fact]
        public async Task SectionsRetainTheirGeometryAndRenderSelectedPages()
        {
            WordDocument document = Read("nextPage");
            Assert.Equal(2, document.Sections.Count);
            WordLayout layout = WordRenderer.Layout(document);
            Assert.Equal(2, layout.PageCount);
            Assert.Equal(new WordPageSize(120, 120), layout.GetPageSize(1));
            Assert.Equal(new WordPageSize(180, 100), layout.GetPageSize(2));
            using PixelBuffer first = await WordRenderer.RenderAsync(layout, 1, new() { Dpi = 72 });
            using PixelBuffer second = await WordRenderer.RenderAsync(layout, 2, new() { Dpi = 72 });
            Assert.Equal(120, first.Width);
            Assert.Equal(180, second.Width);
            Assert.Equal(100, second.Height);
            Assert.Contains((byte)0, first.Pixels.ToArray());
            Assert.Contains((byte)0, second.Pixels.ToArray());
        }

        [Theory]
        [InlineData("evenPage", 2)]
        [InlineData("oddPage", 3)]
        public void SectionParityInsertsOnlyRequiredBlankPages(string type, int pages)
        {
            WordLayout layout = WordRenderer.Layout(Read(type));
            Assert.Equal(pages, layout.PageCount);
            if (pages == 3) { Assert.Empty(layout.Pages[1].Paint); }
        }

        [Fact]
        public void ContinuousSectionsShareAPageAndGlobalPageLimitsApply()
        {
            WordDocument document = Read("continuous", sameGeometry: true);
            Assert.Single(WordRenderer.Layout(document).Pages);
            Assert.Throws<InvalidDataException>(() => WordRenderer.Layout(Read("nextPage"), new() { MaximumPages = 1 }));
            Assert.Throws<NotSupportedException>(() => WordRenderer.Layout(Read("continuous")));
        }

        [Fact]
        public void WrappedSectionParagraphsAreNotLostAndInvalidTypesAreRejected()
        {
            WordDocument document = Read("nextPage", wrapped: true);
            Assert.Equal(2, WordRenderer.Layout(document).PageCount);
            Assert.Throws<InvalidDataException>(() => Read("unknown"));
            Assert.Throws<ArgumentOutOfRangeException>(() => WordRenderer.Layout(document).GetPageSize(3));
        }

        [Fact]
        public async Task SectionHeadersAreInheritedAndRenderedOnLaterPages()
        {
            string first = WordRenderTestInput.SmallPage.Replace("<w:sectPr>", "<w:sectPr><w:headerReference w:type=\"default\" r:id=\"header\"/>");
            byte[] original = WordTestInput.Create("<w:p><w:pPr>" + first + "</w:pPr><w:r><w:t>First</w:t></w:r></w:p><w:p><w:r><w:t>Second</w:t></w:r></w:p>",
                WordRenderTestInput.Fonts, page: WordRenderTestInput.SmallPage);
            using MemoryStream input = new();
            await input.WriteAsync(original);
            using (ZipArchive package = new(input, ZipArchiveMode.Update, leaveOpen: true))
            {
                const string ns = "http://schemas.openxmlformats.org/package/2006/relationships";
                XDocument relationships;
                await using (Stream stream = await package.GetEntry("content/_rels/report.xml.rels")!.OpenAsync()) { relationships = await XDocument.LoadAsync(stream, LoadOptions.None, CancellationToken.None); }
                relationships.Root!.Add(new XElement(XName.Get("Relationship", ns), new XAttribute("Id", "header"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/header"), new XAttribute("Target", "../resources/header.xml")));
                await WritePartAsync("content/_rels/report.xml.rels", relationships.ToString());
                await WritePartAsync("resources/header.xml", "<w:hdr xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:p><w:r><w:rPr><w:color w:val=\"FF0000\"/></w:rPr><w:t>Header</w:t></w:r></w:p></w:hdr>");
                async Task WritePartAsync(string name, string xml)
                {
                    package.GetEntry(name)?.Delete();
                    await using Stream stream = await package.CreateEntry(name).OpenAsync();
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(xml));
                }
            }
            WordDocument document = WordReader.Read(input.ToArray());
            Assert.Same(document.Sections[0].Header.Default, document.Sections[1].Header.Default);
            WordLayout layout = WordRenderer.Layout(document);
            using PixelBuffer second = await WordRenderer.RenderAsync(layout, 2, new() { Dpi = 72 });
            Assert.Contains(Enumerable.Range(0, second.Width * 10), index =>
                second.Pixels.Span[index * 4] > 200 && second.Pixels.Span[index * 4 + 1] < 100);
        }

        private static WordDocument Read(string type, bool sameGeometry = false, bool wrapped = false)
        {
            string paragraph = "<w:p><w:pPr>" + WordRenderTestInput.SmallPage + "</w:pPr><w:r><w:t>First section</w:t></w:r></w:p>";
            if (wrapped) { paragraph = "<w:sdt><w:sdtContent>" + paragraph + "</w:sdtContent></w:sdt>"; }
            string size = sameGeometry ? "<w:pgSz w:w=\"2400\" w:h=\"2400\"/>" : "<w:pgSz w:w=\"3600\" w:h=\"2000\"/>";
            string section = "<w:sectPr><w:type w:val=\"" + type + "\"/>" + size
                + "<w:pgMar w:top=\"200\" w:bottom=\"200\" w:left=\"200\" w:right=\"200\"/></w:sectPr>";
            return WordReader.Read(WordTestInput.Create(paragraph + "<w:p><w:r><w:t>Second section</w:t></w:r></w:p>", WordRenderTestInput.Fonts, page: section));
        }
    }
}
