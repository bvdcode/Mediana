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
        }
    }
}
