// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.OpenXml;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Mediana.Documents.Word
{
    /// <summary>Extracts DOCX text without pagination, font loading or image decoding. Input is borrowed during enumeration.</summary>
    public class WordTextReader(ReadOnlyMemory<byte> input, OpenXmlPackageOptions? packageOptions = null) : IDocumentTextSource
    {
        private const string WordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        private const string StrictWordNamespace = "http://purl.oclc.org/ooxml/wordprocessingml/main";
        public IEnumerable<DocumentTextBlock> ReadText(DocumentTextOptions? options = null, CancellationToken cancellationToken = default)
            => Read(input, options, packageOptions, cancellationToken);

        public static IEnumerable<DocumentTextBlock> Read(ReadOnlyMemory<byte> input, DocumentTextOptions? options = null,
            OpenXmlPackageOptions? packageOptions = null, CancellationToken cancellationToken = default)
        {
            options ??= new();
            DocumentTextBudget budget = new(options);
            using OpenXmlPackage package = OpenXmlPackage.Open(input, packageOptions, cancellationToken);
            string main = package.MainPart(cancellationToken);
            if (package.ContentType(main) != WordReader.DocumentContentType) { throw new NotSupportedException("The OPC main part is not a DOCX document."); }
            using XmlReader reader = package.OpenXmlReader(main, cancellationToken);
            reader.MoveToContent();
            if (reader.LocalName != "document" || reader.NamespaceURI is not (WordNamespace or StrictWordNamespace))
            { throw new InvalidDataException("Invalid Word document namespace or root."); }
            XNamespace ns = reader.NamespaceURI;
            int section = 0, bodyDepth = -1;
            List<WordTextContainer> containers = [];
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.NamespaceURI != ns.NamespaceName) { continue; }
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "body")
                { bodyDepth = reader.Depth; containers.Add(new(DocumentTextNodeKind.Section, section, bodyDepth)); continue; }
                if (bodyDepth < 0) { continue; }
                if (reader.NodeType == XmlNodeType.EndElement)
                {
                    if (reader.Depth == bodyDepth) { yield break; }
                    if (containers.Count > 1 && containers[^1].Depth == reader.Depth) { containers.RemoveAt(containers.Count - 1); }
                    continue;
                }
                if (reader.NodeType != XmlNodeType.Element) { continue; }
                DocumentTextNodeKind? kind = reader.LocalName switch
                {
                    "tbl" => DocumentTextNodeKind.Table,
                    "tr" => DocumentTextNodeKind.Row,
                    "tc" => DocumentTextNodeKind.Cell,
                    _ => null
                };
                if (kind is DocumentTextNodeKind container && !reader.IsEmptyElement)
                {
                    int index = containers[^1].NextIndex++;
                    containers.Add(new(container, index, reader.Depth));
                    continue;
                }
                if (reader.LocalName != "p") { continue; }
                int paragraphIndex = containers[^1].NextIndex++;
                XElement paragraph;
                using (XmlReader subtree = reader.ReadSubtree()) { paragraph = XElement.Load(subtree, LoadOptions.PreserveWhitespace); }
                if (options.IncludesPart(section))
                {
                    DocumentTextNodeKind paragraphKind = paragraph.Element(ns + "pPr")?.Element(ns + "numPr") is not null
                        ? DocumentTextNodeKind.ListItem : DocumentTextNodeKind.Paragraph;
                    if (paragraph.Element(ns + "pPr")?.Element(ns + "outlineLvl") is XElement level
                        && int.TryParse((string?)level.Attribute(ns + "val"), NumberStyles.None, CultureInfo.InvariantCulture, out int outline) && outline < 9)
                    { paragraphKind = DocumentTextNodeKind.Heading; }
                    DocumentTextPathSegment[] path = containers.Select(item => item.Segment).Append(new(paragraphKind, paragraphIndex)).ToArray();
                    string text = ParagraphText(paragraph, ns);
                    budget.Consume(text, cancellationToken);
                    yield return new(text, section, main, Array.AsReadOnly(path), DocumentTextStructure.Declared);
                    int boxIndex = 0;
                    foreach (XElement box in paragraph.Descendants(ns + "txbxContent").Where(item => !item.Ancestors(ns + "txbxContent").Any()))
                    {
                        int index = 0;
                        foreach (XElement nested in box.Descendants(ns + "p"))
                        {
                            string contents = ParagraphText(nested, ns);
                            budget.Consume(contents, cancellationToken);
                            yield return new(contents, section, main, Array.AsReadOnly(path.Concat(new[]
                            {
                                new DocumentTextPathSegment(DocumentTextNodeKind.TextBox, boxIndex),
                                new DocumentTextPathSegment(DocumentTextNodeKind.Paragraph, index++)
                            }).ToArray()), DocumentTextStructure.Declared);
                        }
                        boxIndex++;
                    }
                }
                if (paragraph.Element(ns + "pPr")?.Element(ns + "sectPr") is not null)
                {
                    section++;
                    if (options.PartCount is int count && (long)section - options.StartPartIndex >= count) { yield break; }
                    containers[0] = new(DocumentTextNodeKind.Section, section, bodyDepth);
                }
            }
            if (bodyDepth < 0) { throw new InvalidDataException("Word document has no body."); }
        }

        private static string ParagraphText(XElement paragraph, XNamespace ns)
        {
            StringBuilder text = new();
            foreach (XElement element in paragraph.Descendants())
            {
                if (element.Name.Namespace != ns || element.Ancestors(ns + "p").FirstOrDefault() != paragraph
                    || element.Ancestors(ns + "del").Any() || element.Ancestors(ns + "moveFrom").Any()) { continue; }
                switch (element.Name.LocalName)
                {
                    case "t": text.Append(element.Value); break;
                    case "tab": text.Append('\t'); break;
                    case "br": case "cr": text.Append('\n'); break;
                    case "noBreakHyphen": text.Append('\u2011'); break;
                    case "softHyphen": text.Append('\u00AD'); break;
                    case "sym": throw new NotSupportedException("Font-encoded Word symbols require a Unicode character mapping.");
                }
            }
            return text.ToString();
        }
    }
}
