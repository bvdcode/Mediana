// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.OpenXml;
using System.Xml.Linq;
namespace Mediana.Documents.Presentation
{
    internal class PresentationScene
    {
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
            XElement? themeRoot = theme is null ? null : document.Package.ReadXml(OpenXmlPackage.Resolve(Master!.Name, theme), token).Root;
            Theme = new(themeRoot, Master?.Root, Slide.Root);
            Defaults = document.Defaults;
            PresentationPart? Related(PresentationPart source, string relationship, string root)
            {
                OpenXmlRelationship? link = source.Relationships.SingleOrDefault(item => OpenXmlNamespaces.IsOfficeRelationship(item.Type, relationship));
                return link is null ? null : document.ReadPart(OpenXmlPackage.Resolve(source.Name, link), root, token);
            }
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
