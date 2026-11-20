// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.Word;
using System.IO.Compression;
using System.Text;

namespace Mediana.Tests
{
    internal static class WordTestInput
    {
        internal const string Page = "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:right=\"1440\"/></w:sectPr>";

        internal const string PictureBody = """
                <w:p><w:r><w:drawing><wp:inline xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing">
                  <wp:extent cx="254000" cy="254000"/><wp:docPr id="1" name="Picture"/>
                  <a:graphic xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture">
                    <pic:pic xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:nvPicPr><pic:cNvPr id="0" name="Picture"/><pic:cNvPicPr/></pic:nvPicPr>
                    <pic:blipFill><a:blip r:embed="picture"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>
                    <pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="254000" cy="254000"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr>
                    </pic:pic></a:graphicData></a:graphic>
                </wp:inline></w:drawing></w:r></w:p>
                """;

        internal static byte[] Create(string body, string? styles = null, string? numbering = null, string? page = Page, byte[]? picture = null)
        {
            const string word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            const string relations = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
            using MemoryStream output = new();
            using (ZipArchive archive = new(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                Write("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"png\" ContentType=\"image/png\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>"
                    + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Override PartName=\"/content/report.xml\" ContentType=\""
                    + WordReader.DocumentContentType + "\"/></Types>");
                Write("_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"main\" Type=\""
                    + relations + "officeDocument\" Target=\"content/report.xml\"/></Relationships>");
                Write("content/report.xml", "<w:document xmlns:w=\"" + word + "\" xmlns:r=\"" + relations.TrimEnd('/') + "\"><w:body>" + body + page + "</w:body></w:document>");
                StringBuilder linked = new("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
                if (styles is not null) { Part("styles", styles); }
                if (numbering is not null) { Part("numbering", numbering); }
                if (picture is not null)
                {
                    using Stream image = archive.CreateEntry("resources/picture.png", CompressionLevel.Fastest).Open();
                    image.Write(picture);
                    linked.Append("<Relationship Id=\"picture\" Type=\"").Append(relations).Append("image\" Target=\"../resources/picture.png\"/>");
                }
                linked.Append("</Relationships>");
                Write("content/_rels/report.xml.rels", linked.ToString());

                void Part(string kind, string content)
                {
                    Write("resources/" + kind + ".xml", "<w:" + kind + " xmlns:w=\"" + word + "\">" + content + "</w:" + kind + ">");
                    linked.Append("<Relationship Id=\"").Append(kind).Append("\" Type=\"").Append(relations).Append(kind)
                        .Append("\" Target=\"../resources/").Append(kind).Append(".xml\"/>");
                }
                void Write(string name, string text)
                {
                    using Stream stream = archive.CreateEntry(name, CompressionLevel.Fastest).Open();
                    stream.Write(Encoding.UTF8.GetBytes(text));
                }
            }
            return output.ToArray();
        }

        internal static void Verify()
        {
            byte[] input = Create("<w:p><w:r><w:rPr><w:rFonts w:ascii=\"Canada1500\"/><w:sz w:val=\"9pt\"/></w:rPr><w:t>Document</w:t></w:r></w:p>");
            WordDocument document = WordReader.Read(input);
            WordText text = (WordText)((WordParagraph)document.Sections.Single().Blocks[0]).Content[0];
            if (document.PageSize != WordPageSize.Letter || text.Text != "Document" || text.Format.Size != 9 || document.Warnings.Count != 0)
            {
                throw new InvalidDataException("Word reading must retain page, text and unit-bearing font size.");
            }
            WordDocument styled = WordReader.Read(Create("<w:tbl><w:tblPr><w:tblStyle w:val='report'/><w:tblLook w:firstRow='1'/></w:tblPr><w:tblGrid><w:gridCol w:w='1000'/></w:tblGrid>"
                + "<w:tr><w:tc><w:p/></w:tc></w:tr><w:tr><w:tc><w:p/></w:tc></w:tr></w:tbl>",
                "<w:style w:type='table' w:styleId='report'><w:tblPr><w:tblBorders><w:top w:val='single' w:sz='8' w:color='4F81BD'/></w:tblBorders></w:tblPr>"
                + "<w:tblStylePr w:type='band1Horz'><w:tcPr><w:shd w:fill='D3DFEE'/></w:tcPr></w:tblStylePr></w:style>"));
            WordTable table = (WordTable)styled.Sections[0].Blocks[0];
            if (table.Rows[0].Cells[0].Shading is not null || table.Rows[1].Cells[0].Shading != new Drawing.Rgba32(211, 223, 238)
                || table.Rows[0].Cells[0].Borders.Top?.Color != new Drawing.Rgba32(79, 129, 189))
            { throw new InvalidDataException("Word table styles must retain conditional fills and table borders."); }
        }
    }
}
