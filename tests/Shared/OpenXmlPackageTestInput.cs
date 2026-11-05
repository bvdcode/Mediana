// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.OpenXml;
using System.IO.Compression;
using System.Text;

namespace Mediana.Tests
{
    internal static class OpenXmlPackageTestInput
    {
        internal static byte[] Create(string document = "<document/>", string mainPart = "content/report.xml", string target = "content/report.xml", string? extra = null)
        {
            using MemoryStream output = new();
            using (ZipArchive archive = new(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                Write(archive, "[Content_Types].xml", """
                    <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                      <Default Extension="xml" ContentType="application/xml"/>
                      <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                    </Types>
                    """);
                Write(archive, "_rels/.rels", """
                    <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="r1"
                    Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="
                    """ + target + "\"/></Relationships>");
                Write(archive, mainPart, document);
                if (extra is not null) { Write(archive, extra, "<extra/>"); }
            }
            return output.ToArray();
        }

        internal static void Verify()
        {
            byte[] encoded = Create("<document><paragraph>Hello</paragraph></document>");
            byte[] original = encoded.ToArray();
            using OpenXmlPackage package = OpenXmlPackage.Open(encoded);
            string main = package.MainPart();
            if (main != "content/report.xml" || package.ContentType(main) != "application/xml"
                || package.ReadXml(main).Root?.Element("paragraph")?.Value != "Hello" || !encoded.SequenceEqual(original))
            {
                throw new InvalidDataException("OPC reading must follow relationships and preserve source storage.");
            }
            OpenXmlRelationship related = new("r2", "image", "../media/picture%20one.png", false);
            if (OpenXmlPackage.Resolve(main, related) != "media/picture%20one.png")
            {
                throw new InvalidDataException("OPC relative targets must resolve from their source part.");
            }
        }

        private static void Write(ZipArchive archive, string name, string text)
        {
            using Stream stream = archive.CreateEntry(name, CompressionLevel.Fastest).Open();
            stream.Write(Encoding.UTF8.GetBytes(text));
        }
    }
}
