// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using System.Text;
namespace Mediana.Tests
{
    internal static class DocumentPackageTestData
    {
        internal const string Relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
        internal const string Office = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
        internal static byte[] Create(string contentType, string main, Dictionary<string, string> parts, Dictionary<string, byte[]>? binaries = null)
        {
            using MemoryStream buffer = new();
            using (ZipArchive archive = new(buffer, ZipArchiveMode.Create, true))
            {
                Write("[Content_Types].xml", Encoding.UTF8.GetBytes("<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'><Default Extension='xml' ContentType='application/xml'/><Default Extension='rels' ContentType='application/vnd.openxmlformats-package.relationships+xml'/><Default Extension='bin' ContentType='application/octet-stream'/><Override PartName='/" + main + "' ContentType='" + contentType + "'/></Types>"));
                Write("_rels/.rels", Encoding.UTF8.GetBytes("<Relationships xmlns='" + Relationships + "'><Relationship Id='main' Type='" + Office + "officeDocument' Target='" + main + "'/></Relationships>"));
                foreach ((string name, string xml) in parts) { Write(name, Encoding.UTF8.GetBytes(xml)); }
                if (binaries is not null) { foreach ((string name, byte[] bytes) in binaries) { Write(name, bytes); } }
                void Write(string name, byte[] bytes) { using Stream stream = archive.CreateEntry(name).Open(); stream.Write(bytes); }
            }
            return buffer.ToArray();
        }
        internal static byte[] WordStories(string? header = null, bool strict = false, string? firstReferences = null, string? secondReferences = null)
        {
            const string w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            string attributes = "xmlns:w='" + w + "' xmlns:r='" + Office.TrimEnd('/') + "'";
            const string geometry = "<w:pgSz w:w='12000' w:h='16000'/><w:pgMar w:left='1000' w:right='1000' w:top='1000' w:bottom='1000'/>";
            string references = firstReferences ?? "<w:headerReference w:type='default' r:id='h'/><w:headerReference w:type='first' r:id='first'/><w:headerReference w:type='even' r:id='even'/><w:footerReference w:type='default' r:id='f'/>";
            Dictionary<string, string> parts = new()
            {
                ["word/main.xml"] = "<w:document " + attributes + "><w:body><w:p><w:pPr><w:sectPr>" + references + geometry + "</w:sectPr></w:pPr><w:r><w:t>Body 1</w:t></w:r></w:p><w:p><w:r><w:t>Body 2</w:t></w:r></w:p><w:sectPr>" + secondReferences + geometry + "</w:sectPr></w:body></w:document>",
                ["word/_rels/main.xml.rels"] = Relations(("h", "header", "headers/default.xml"), ("first", "header", "headers/first.xml"), ("even", "header", "headers/even.xml"), ("f", "footer", "footers/default.xml")),
                ["word/headers/default.xml"] = "<w:hdr " + attributes + ">" + (header ?? "<w:p><w:r><w:t>Заголовок 😀 é</w:t><w:tab/><w:t>終</w:t><w:br/><w:t>line</w:t></w:r></w:p>") + "</w:hdr>",
                ["word/headers/first.xml"] = "<w:hdr " + attributes + "><w:p><w:r><w:t>First</w:t></w:r></w:p></w:hdr>",
                ["word/headers/even.xml"] = "<w:hdr " + attributes + "><w:p><w:r><w:t>Even</w:t></w:r></w:p></w:hdr>",
                ["word/footers/default.xml"] = "<w:ftr " + attributes + "><w:p><w:r><w:t>Footer</w:t></w:r></w:p></w:ftr>"
            };
            if (strict)
            {
                parts = parts.ToDictionary(item => item.Key, item => item.Value.Replace(w, "http://purl.oclc.org/ooxml/wordprocessingml/main", StringComparison.Ordinal)
                    .Replace(Office.TrimEnd('/'), "http://purl.oclc.org/ooxml/officeDocument/relationships", StringComparison.Ordinal));
            }
            return Create(Documents.Word.WordReader.DocumentContentType, "word/main.xml", parts);
        }

        internal static byte[] PresentationNotes(string? notes = null, bool strict = false, string? notesRelationship = null,
            string slideSize = "<p:sldSz cx='1828800' cy='1371600'/>")
        {
            string attributes = "xmlns:p='http://schemas.openxmlformats.org/presentationml/2006/main' xmlns:a='http://schemas.openxmlformats.org/drawingml/2006/main' xmlns:r='" + Office.TrimEnd('/') + "'";
            Dictionary<string, string> parts = new()
            {
                ["ppt/main.xml"] = "<p:presentation " + attributes + "><p:sldIdLst><p:sldId id='256' r:id='s'/><p:sldId id='257' r:id='bad'/></p:sldIdLst>" + slideSize + "</p:presentation>",
                ["ppt/_rels/main.xml.rels"] = Relations(("s", "slide", "slides/one.xml"), ("bad", "slide", "slides/bad.xml")),
                ["ppt/slides/one.xml"] = "<p:sld " + attributes + "><p:cSld><p:spTree><p:sp><p:txBody><a:p><a:r><a:t>Slide</a:t></a:r></a:p></p:txBody></p:sp><p:pic><p:blipFill><a:blip r:embed='broken'/></p:blipFill></p:pic></p:spTree></p:cSld></p:sld>",
                ["ppt/slides/bad.xml"] = "<broken",
                ["ppt/slides/_rels/one.xml.rels"] = notesRelationship ?? Relations(("n", "notesSlide", "../notes/one.xml"), ("broken", "image", "../media/missing.bin")),
                ["ppt/notes/one.xml"] = "<p:notes " + attributes + "><p:cSld><p:spTree>" + (notes ??
                    "<p:sp><p:nvSpPr><p:nvPr><p:ph type='sldNum'/></p:nvPr></p:nvSpPr><p:txBody><a:p><a:r><a:t>42</a:t></a:r></a:p></p:txBody></p:sp>"
                    + "<p:sp><p:nvSpPr><p:nvPr><p:ph type='body'/></p:nvPr></p:nvSpPr><p:txBody><a:p><a:r><a:rPr><a:latin typeface='Missing font'/></a:rPr><a:t>Заметки 😀 é</a:t></a:r><a:br/><a:fld id='field'><a:t>終</a:t></a:fld></a:p></p:txBody></p:sp>") + "</p:spTree></p:cSld></p:notes>"
            };
            if (strict)
            {
                parts = parts.ToDictionary(item => item.Key, item => item.Value
                    .Replace("http://schemas.openxmlformats.org/presentationml/2006/main", "http://purl.oclc.org/ooxml/presentationml/main", StringComparison.Ordinal)
                    .Replace("http://schemas.openxmlformats.org/drawingml/2006/main", "http://purl.oclc.org/ooxml/drawingml/main", StringComparison.Ordinal)
                    .Replace(Office.TrimEnd('/'), "http://purl.oclc.org/ooxml/officeDocument/relationships", StringComparison.Ordinal));
            }
            return Create(Documents.Presentation.PresentationReader.DocumentContentType, "ppt/main.xml", parts);
        }

        internal static string Relations(params (string Id, string Kind, string Target)[] entries) => "<Relationships xmlns='" + Relationships + "'>"
            + string.Concat(entries.Select(item => "<Relationship Id='" + item.Id + "' Type='" + Office + item.Kind + "' Target='" + item.Target + "'/>")) + "</Relationships>";
    }
}
