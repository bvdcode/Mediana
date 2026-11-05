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
            VerifyXmlLimits();
            OpenXmlRelationship related = new("r2", "image", "../media/picture%20one.png", false);
            if (OpenXmlPackage.Resolve(main, related) != "media/picture%20one.png")
            {
                throw new InvalidDataException("OPC relative targets must resolve from their source part.");
            }
        }

        private static void VerifyXmlLimits()
        {
            const string xml = "<a xmlns='urn:sample' x='1'> text<![CDATA[value]]><b/></a>";
            using OpenXmlPackage bounded = OpenXmlPackage.Open(Create(xml), new() { MaximumXmlElements = 7 });
            for (int read = 0; read < 2; read++)
            {
                System.Xml.Linq.XElement root = bounded.ReadXml("content/report.xml").Root!;
                if (root.Name.NamespaceName != "urn:sample" || root.Value != " textvalue" || root.Attribute("x")?.Value != "1")
                {
                    throw new InvalidDataException("OPC XML must preserve namespaces, attributes and mixed content.");
                }
            }
            bool rejected = false;
            try { bounded.ReadXml("content/report.xml"); }
            catch (InvalidDataException exception)
            {
                System.Diagnostics.Trace.TraceInformation("Expected cumulative XML budget rejection: {0}", exception.Message);
                rejected = true;
            }
            if (!rejected) { throw new InvalidDataException("OPC must count XML elements across repeated reads."); }
            using OpenXmlPackage depth = OpenXmlPackage.Open(Create("<a><b><c><d/></c></b></a>"), new() { MaximumXmlDepth = 2 });
            rejected = false;
            try { depth.ReadXml("content/report.xml"); }
            catch (InvalidDataException exception)
            {
                System.Diagnostics.Trace.TraceInformation("Expected XML depth rejection: {0}", exception.Message);
                rejected = true;
            }
            if (!rejected) { throw new InvalidDataException("OPC must enforce XML depth before consuming a node."); }
        }

        private static void Write(ZipArchive archive, string name, string text)
        {
            using Stream stream = archive.CreateEntry(name, CompressionLevel.Fastest).Open();
            stream.Write(Encoding.UTF8.GetBytes(text));
        }
    }
}
