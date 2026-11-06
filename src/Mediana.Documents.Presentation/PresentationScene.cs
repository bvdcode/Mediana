// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.OpenXml;
using System.Xml.Linq;
namespace Mediana.Documents.Presentation
{
    internal class PresentationScene
    {
        private readonly IReadOnlyList<PresentationPart> _parts;
        internal PresentationPart Slide { get; }
        internal PresentationPart? Layout { get; }
        internal PresentationPart? Master { get; }
        internal PresentationTheme Theme { get; }
        internal XElement Defaults { get; }
        internal PresentationScene(PresentationDocument document, int index, CancellationToken token)
        {
            Slide = document.ReadPart(document.Slides[index].PartName, "sld", token);
            Layout = Related(Slide, "slideLayout", "sldLayout");
            Master = Layout is null ? null : Related(Layout, "slideMaster", "sldMaster");
            OpenXmlRelationship? theme = Master?.Relationships.SingleOrDefault(item => OpenXmlNamespaces.IsOfficeRelationship(item.Type, "theme"));
            PresentationPart? themePart = theme is null ? null : document.ReadTheme(OpenXmlPackage.Resolve(Master!.Name, theme), token);
            Theme = new(themePart?.Root, Master?.Root, Slide.Root);
            _parts = new[] { Slide, Layout, Master, themePart }.OfType<PresentationPart>().ToArray();
            Defaults = document.Defaults;
            PresentationPart? Related(PresentationPart source, string relationship, string root)
            {
                OpenXmlRelationship? link = source.Relationships.SingleOrDefault(item => OpenXmlNamespaces.IsOfficeRelationship(item.Type, relationship));
                return link is null ? null : document.ReadPart(OpenXmlPackage.Resolve(source.Name, link), root, token);
            }
        }
        internal PresentationPart SourceOf(XElement element)
        {
            XElement root = element.AncestorsAndSelf().Last();
            return _parts.FirstOrDefault(part => ReferenceEquals(part.Root, root))
                ?? throw new InvalidDataException("Presentation resource has no owning part.");
        }
        internal IReadOnlyList<XElement> ShapeChain(XElement shape)
        {
            List<XElement> chain = [shape];
            XElement? ph = PresentationXml.Placeholder(shape);
            if (ph is null) { return chain; }
            XElement? layout = Find(Layout, ph, true);
            if (layout is not null) { chain.Add(layout); }
            XElement? master = Find(Master, layout is null ? ph : PresentationXml.Placeholder(layout)!, false);
            if (master is not null) { chain.Add(master); }
            return chain;
        }
        private static XElement? Find(PresentationPart? part, XElement ph, bool byIndex)
        {
            string index = (string?)ph.Attribute("idx") ?? "0", type = (string?)ph.Attribute("type") ?? "obj";
            return part?.Tree?.Elements().FirstOrDefault(item => PresentationXml.Placeholder(item) is XElement candidate
                && (byIndex ? ((string?)candidate.Attribute("idx") ?? "0") == index
                    : ((string?)candidate.Attribute("type") ?? "obj") == type));
        }
    }
}
