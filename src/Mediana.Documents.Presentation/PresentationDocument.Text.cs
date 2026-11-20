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
        /// <summary>Yields stored slide text and optional speaker notes in declared shape order, without theme, font or picture decoding.</summary>
        public IEnumerable<DocumentTextBlock> ReadText(DocumentTextOptions? options = null, CancellationToken cancellationToken = default)
        {
            options ??= new();
            DocumentTextBudget budget = new(options);
            cancellationToken.ThrowIfCancellationRequested();
            _ = Package;
            for (int index = options.StartPartIndex; index < Slides.Count && options.IncludesPart(index); index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (DocumentTextBlock block in ReadTextPart(Slides[index].PartName, "sld", index, DocumentTextSourceKind.Body, budget, cancellationToken)) { yield return block; }
                if (!options.IncludeSpeakerNotes) { continue; }
                cancellationToken.ThrowIfCancellationRequested();
                OpenXmlRelationship[] notes = Package.ReadRelationships(Slides[index].PartName, cancellationToken)
                    .Where(relation => OpenXmlNamespaces.IsOfficeRelationship(relation.Type, "notesSlide")).ToArray();
                if (notes.Length > 1) { throw new InvalidDataException("A slide has multiple notes relationships."); }
                if (notes.Length == 0) { continue; }
                string part = Package.CanonicalPart(OpenXmlPackage.Resolve(Slides[index].PartName, notes[0]));
                foreach (DocumentTextBlock block in ReadTextPart(part, "notes", index, DocumentTextSourceKind.SpeakerNotes, budget, cancellationToken)) { yield return block; }
            }
        }

        private IEnumerable<DocumentTextBlock> ReadTextPart(string part, string rootName, int index,
            DocumentTextSourceKind sourceKind, DocumentTextBudget budget, CancellationToken cancellationToken)
        {
            using XmlReader reader = Package.OpenXmlReader(part, cancellationToken);
            reader.MoveToContent();
            if (reader.LocalName != rootName || reader.NamespaceURI is not (OpenXmlNamespaces.Presentation or OpenXmlNamespaces.StrictPresentation))
            { throw new InvalidDataException("Invalid presentation slide root."); }
            string p = reader.NamespaceURI;
            string a = p == OpenXmlNamespaces.Presentation ? OpenXmlNamespaces.Drawing : OpenXmlNamespaces.StrictDrawing;
            List<PresentationTextContainer> containers = [new(DocumentTextNodeKind.Slide, index, reader.Depth)];
            if (sourceKind == DocumentTextSourceKind.SpeakerNotes) { containers.Add(new(DocumentTextNodeKind.SpeakerNotes, 0, -1)); }
            int excludedDepth = -1;
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (excludedDepth >= 0)
                {
                    if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == excludedDepth)
                    {
                        excludedDepth = -1;
                        containers.RemoveAt(containers.Count - 1);
                    }
                    continue;
                }
                if (reader.NodeType == XmlNodeType.EndElement)
                {
                    if (containers.Count > 1 && containers[^1].Depth == reader.Depth) { containers.RemoveAt(containers.Count - 1); }
                    continue;
                }
                if (reader.NodeType != XmlNodeType.Element) { continue; }
                if (sourceKind == DocumentTextSourceKind.SpeakerNotes && reader.NamespaceURI == p && reader.LocalName == "ph"
                    && reader.GetAttribute("type") is "sldImg" or "sldNum" or "hdr" or "ftr" or "dt")
                {
                    if (containers[^1].Segment.Kind == DocumentTextNodeKind.TextBox) { excludedDepth = containers[^1].Depth; }
                    continue;
                }
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
                foreach (XElement run in paragraph.Elements().Where(element => element.Name.NamespaceName == a))
                {
                    if (run.Name.LocalName is "r" or "fld") { text.Append(run.Element(XName.Get("t", a))?.Value); }
                    else if (run.Name.LocalName == "br") { text.Append('\n'); }
                }
                string value = text.ToString();
                budget.Consume(value, cancellationToken);
                DocumentTextPathSegment[] path = containers.Select(item => item.Segment).Append(new(DocumentTextNodeKind.Paragraph, containers[^1].NextIndex++)).ToArray();
                yield return new(value, index, part, Array.AsReadOnly(path), DocumentTextStructure.Declared) { SourceKind = sourceKind };
            }
        }
    }
}
