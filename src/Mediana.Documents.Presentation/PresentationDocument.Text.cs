// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.OpenXml;
using System.Text;
using System.Xml;
using System.Xml.Linq;
namespace Mediana.Documents.Presentation
{
    public partial class PresentationDocument
    {
        /// <summary>Yields stored slide text in declared shape order, without theme, font or picture decoding.</summary>
        public IEnumerable<DocumentTextBlock> ReadText(DocumentTextOptions? options = null, CancellationToken cancellationToken = default)
        {
            options ??= new();
            DocumentTextBudget budget = new(options);
            for (int index = options.StartPartIndex; index < Slides.Count && options.IncludesPart(index); index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using XmlReader reader = Package.OpenXmlReader(Slides[index].PartName, cancellationToken);
                reader.MoveToContent();
                if (reader.LocalName != "sld" || reader.NamespaceURI is not (OpenXmlNamespaces.Presentation or OpenXmlNamespaces.StrictPresentation))
                { throw new InvalidDataException("Invalid presentation slide root."); }
                string p = reader.NamespaceURI;
                string a = p == OpenXmlNamespaces.Presentation ? OpenXmlNamespaces.Drawing : OpenXmlNamespaces.StrictDrawing;
                List<PresentationTextContainer> containers = [new(DocumentTextNodeKind.Slide, index, reader.Depth)];
                while (reader.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (reader.NodeType == XmlNodeType.EndElement)
                    {
                        if (containers.Count > 1 && containers[^1].Depth == reader.Depth) { containers.RemoveAt(containers.Count - 1); }
                        continue;
                    }
                    if (reader.NodeType != XmlNodeType.Element) { continue; }
                    DocumentTextNodeKind? kind = reader.NamespaceURI == p ? reader.LocalName switch
                    {
                        "grpSp" => DocumentTextNodeKind.Section,
                        "sp" => DocumentTextNodeKind.TextBox,
                        _ => null
                    } : reader.NamespaceURI == a ? reader.LocalName switch
                    {
                        "tbl" => DocumentTextNodeKind.Table,
                        "tr" => DocumentTextNodeKind.Row,
                        "tc" => DocumentTextNodeKind.Cell,
                        _ => null
                    } : null;
                    if (kind is DocumentTextNodeKind container && !reader.IsEmptyElement)
                    { containers.Add(new(container, containers[^1].NextIndex++, reader.Depth)); continue; }
                    if (reader.NamespaceURI != a || reader.LocalName != "p") { continue; }
                    XElement paragraph;
                    using (XmlReader subtree = reader.ReadSubtree()) { paragraph = XElement.Load(subtree, LoadOptions.PreserveWhitespace); }
                    StringBuilder text = new();
                    foreach (XElement run in paragraph.Elements())
                    {
                        if (run.Name.LocalName is "r" or "fld") { text.Append(run.Element(XName.Get("t", a))?.Value); }
                        else if (run.Name.LocalName == "br") { text.Append('\n'); }
                    }
                    string value = text.ToString();
                    budget.Consume(value, cancellationToken);
                    DocumentTextPathSegment[] path = containers.Select(item => item.Segment).Append(new(DocumentTextNodeKind.Paragraph, containers[^1].NextIndex++)).ToArray();
                    yield return new(value, index, Slides[index].PartName, Array.AsReadOnly(path), DocumentTextStructure.Declared);
                }
            }
        }
    }
}
