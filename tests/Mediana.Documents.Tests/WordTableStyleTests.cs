// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.Word;
using Mediana.Drawing;
using Mediana.Tests;
using Xunit;

namespace Mediana.Documents.Tests
{
    public class WordTableStyleTests
    {
        private const string Base = """
            <w:style w:type='table' w:styleId='base' w:default='1'>
              <w:rPr><w:rFonts w:ascii='Canada1500'/><w:color w:val='365F91'/></w:rPr>
              <w:tblPr><w:tblCellMar><w:left w:w='80'/></w:tblCellMar><w:tblBorders><w:top w:val='single' w:sz='8' w:color='4F81BD'/><w:bottom w:val='single' w:sz='8' w:color='4F81BD'/></w:tblBorders></w:tblPr>
              <w:tblStylePr w:type='firstRow'><w:rPr><w:b/></w:rPr><w:tcPr><w:tcBorders><w:bottom w:val='single' w:sz='8' w:color='4F81BD'/></w:tcBorders></w:tcPr></w:tblStylePr>
              <w:tblStylePr w:type='band1Horz'><w:tcPr><w:shd w:fill='D3DFEE'/></w:tcPr></w:tblStylePr>
            </w:style>
            """;

        [Fact]
        public void InheritedStylesRetainTableBordersBandingMarginsAndRunFormatting()
        {
            WordTable table = Read("<w:tblStyle w:val='derived'/><w:tblLook w:firstRow='1' w:noVBand='1'/>", Base
                + "<w:style w:type='table' w:styleId='derived'><w:basedOn w:val='base'/><w:tblPr><w:tblCellMar><w:right w:w='60'/></w:tblCellMar></w:tblPr></w:style>");
            Assert.Equal(new Rgba32(79, 129, 189), table.Rows[0].Cells[0].Borders.Bottom!.Color);
            Assert.Equal(1, table.Rows[0].Cells[0].Borders.Top!.Width);
            Assert.Null(table.Rows[0].Cells[0].Shading);
            Assert.Equal(new Rgba32(211, 223, 238), table.Rows[1].Cells[0].Shading);
            Assert.Null(table.Rows[2].Cells[0].Shading);
            Assert.Equal(new Rgba32(211, 223, 238), table.Rows[3].Cells[0].Shading);
            Assert.Equal(4, table.Rows[1].Cells[0].Left);
            Assert.Equal(3, table.Rows[1].Cells[0].Right);
            WordText header = Text(table, 0, 0), body = Text(table, 1, 0);
            Assert.True(header.Format.Bold);
            Assert.False(body.Format.Bold);
            Assert.Equal(new Rgba32(54, 95, 145), body.Format.Color);
            Assert.Equal("Canada1500", body.Format.Family);
        }

        [Fact]
        public void RegionPriorityAndDirectCellPropertiesOverrideInheritedFormats()
        {
            string styles = Base + """
                <w:style w:type='table' w:styleId='derived'><w:basedOn w:val='base'/>
                  <w:tblStylePr w:type='wholeTable'><w:tcPr><w:shd w:fill='111111'/></w:tcPr></w:tblStylePr>
                  <w:tblStylePr w:type='firstRow'><w:tcPr><w:shd w:fill='222222'/></w:tcPr></w:tblStylePr>
                  <w:tblStylePr w:type='firstCol'><w:tcPr><w:shd w:fill='333333'/></w:tcPr></w:tblStylePr>
                  <w:tblStylePr w:type='nwCell'><w:tcPr><w:shd w:fill='444444'/></w:tcPr></w:tblStylePr>
                </w:style>
                """;
            WordTable table = Read("<w:tblStyle w:val='derived'/><w:tblLook w:val='06A0'/>", styles);
            Assert.Equal(new Rgba32(68, 68, 68), table.Rows[0].Cells[0].Shading);
            Assert.Equal(new Rgba32(34, 34, 34), table.Rows[0].Cells[1].Shading);
            Assert.Equal(new Rgba32(51, 51, 51), table.Rows[1].Cells[0].Shading);
            table = Read("<w:tblStyle w:val='derived'/><w:tblLook w:val='06A0'/>", styles,
                "<w:shd w:fill='FF0000'/><w:tcBorders><w:bottom w:val='nil'/></w:tcBorders>");
            Assert.Equal(new Rgba32(255, 0, 0), table.Rows[0].Cells[0].Shading);
            Assert.Null(table.Rows[0].Cells[0].Borders.Bottom);
        }

        [Fact]
        public void DefaultTableStyleAndExplicitLookFlagsControlBanding()
        {
            WordTable table = Read("<w:tblLook w:val='0020' w:firstRow='0'/>", Base);
            Assert.Equal(new Rgba32(211, 223, 238), table.Rows[0].Cells[0].Shading);
            Assert.False(Text(table, 0, 0).Format.Bold);
            table = Read("<w:tblLook w:noHBand='1'/>", Base);
            Assert.All(table.Rows, row => Assert.All(row.Cells, cell => Assert.Null(cell.Shading)));
            table = Read("<w:tblStyleRowBandSize w:val='2'/>", Base);
            Assert.NotNull(table.Rows[0].Cells[0].Shading);
            Assert.NotNull(table.Rows[1].Cells[0].Shading);
            Assert.Null(table.Rows[2].Cells[0].Shading);
            Assert.Null(table.Rows[3].Cells[0].Shading);
        }

        [Fact]
        public async Task ConditionalShadingAndBordersReachRenderedPixels()
        {
            WordDocument document = Document("<w:tblLook w:firstRow='1' w:noVBand='1'/>", Base);
            WordLayout layout = WordRenderer.Layout(document);
            using PixelBuffer image = await WordRenderer.RenderAsync(layout, 1, new() { Dpi = 72, Supersampling = 1 });
            Assert.Equal(new Rgba32(79, 129, 189), image[20, 10]);
            Assert.Equal(new Rgba32(79, 129, 189), image[35, 29]);
            Assert.Equal(new Rgba32(211, 223, 238), image[35, 35]);
            Assert.Equal(new Rgba32(255, 255, 255), image[35, 55]);
        }

        [Theory]
        [InlineData("<w:tblLook w:val='zzzz'/>")]
        [InlineData("<w:tblLook w:noHBand='invalid'/>")]
        [InlineData("<w:tblStyleRowBandSize w:val='0'/>")]
        public void InvalidTableConditionsAreRejected(string properties) => Assert.Throws<InvalidDataException>(() => Read(properties, Base));

        [Fact]
        public void TableStyleCyclesAreRejected() => Assert.Throws<InvalidDataException>(() => Read("<w:tblStyle w:val='cycle'/>",
            "<w:style w:type='table' w:styleId='cycle'><w:basedOn w:val='cycle'/></w:style>"));

        private static WordText Text(WordTable table, int row, int column) => (WordText)((WordParagraph)table.Rows[row].Cells[column].Blocks[0]).Content[0];
        private static WordTable Read(string properties, string styles, string cell = "") => (WordTable)Document(properties, styles, cell).Sections[0].Blocks[0];
        private static WordDocument Document(string properties, string styles, string cell = "")
        {
            string content = "<w:tc><w:tcPr>" + cell + "</w:tcPr><w:p><w:r><w:t>A</w:t></w:r></w:p></w:tc>";
            string row = "<w:tr><w:trPr><w:trHeight w:val='400' w:hRule='exact'/></w:trPr>" + content + content + "</w:tr>";
            return WordReader.Read(WordTestInput.Create("<w:tbl><w:tblPr>" + properties + "</w:tblPr><w:tblGrid><w:gridCol w:w='800'/><w:gridCol w:w='800'/></w:tblGrid>"
                + row + row + row + row + "</w:tbl>", styles, page: "<w:sectPr><w:pgSz w:w='2400' w:h='2400'/><w:pgMar w:top='200' w:left='200'/></w:sectPr>"));
        }
    }
}
