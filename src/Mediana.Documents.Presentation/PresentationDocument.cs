// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.OpenXml;
using System.Xml.Linq;
namespace Mediana.Documents.Presentation
{
    /// <summary>A single-consumer presentation. Rendering and text extraction use selected slides only.</summary>
    public partial class PresentationDocument : IDisposable, IDocumentTextSource
    {
        private OpenXmlPackage? _package;
        private readonly bool _ownsPackage;
        internal PresentationReaderOptions Options { get; }
        internal XElement Defaults { get; }
        public double Width { get; }
        public double Height { get; }
        public IReadOnlyList<PresentationSlideInfo> Slides { get; }
        internal OpenXmlPackage Package => _package ?? throw new ObjectDisposedException(nameof(PresentationDocument));
        internal PresentationDocument(OpenXmlPackage package, string main, PresentationReaderOptions options, bool ownsPackage, CancellationToken token)
        {
            if (options.MaximumSlides <= 0 || options.MaximumShapes <= 0 || options.MaximumGlyphs <= 0) { throw new ArgumentOutOfRangeException(nameof(options)); }
            _package = package; _ownsPackage = ownsPackage; Options = options;
            if (package.ContentType(main) != PresentationReader.DocumentContentType) { throw new NotSupportedException("The OPC main part is not a PPTX presentation."); }
            XElement root = package.ReadXml(main, token).Root ?? throw new InvalidDataException("Empty presentation.");
            XNamespace p = root.Name.Namespace;
            if (root.Name.LocalName != "presentation" || p != OpenXmlNamespaces.Presentation && p != OpenXmlNamespaces.StrictPresentation)
            { throw new InvalidDataException("Invalid PresentationML root."); }
            XElement size = root.Element(p + "sldSz") ?? throw new InvalidDataException("Presentation slide size is missing.");
            Width = PresentationXml.Points(size, "cx"); Height = PresentationXml.Points(size, "cy");
            if (Width <= 0 || Height <= 0) { throw new InvalidDataException("Invalid presentation slide size."); }
            Defaults = root.Element(p + "defaultTextStyle") ?? new XElement(p + "defaultTextStyle");
            Dictionary<string, OpenXmlRelationship> relationships = package.ReadRelationships(main, token).ToDictionary(item => item.Id, StringComparer.Ordinal);
            HashSet<string> ids = new(StringComparer.Ordinal);
            List<PresentationSlideInfo> slides = [];
            foreach (XElement entry in root.Element(p + "sldIdLst")?.Elements(p + "sldId") ?? [])
            {
                token.ThrowIfCancellationRequested();
                if (slides.Count >= options.MaximumSlides) { throw new InvalidDataException("Presentation exceeds MaximumSlides."); }
                string id = PresentationXml.RelationId(entry, "id");
                if (!ids.Add(id) || !relationships.TryGetValue(id, out OpenXmlRelationship? relation)
                    || !OpenXmlNamespaces.IsOfficeRelationship(relation.Type, "slide")) { throw new InvalidDataException("Invalid presentation slide relationship."); }
                slides.Add(new(slides.Count, package.CanonicalPart(OpenXmlPackage.Resolve(main, relation))));
            }
            Slides = slides.AsReadOnly();
        }
        internal PresentationPart ReadPart(string name, string rootName, CancellationToken token)
        {
            XElement root = Package.ReadXml(name, token).Root ?? throw new InvalidDataException("Empty presentation part.");
            if (root.Name.LocalName != rootName || root.Name.Namespace != OpenXmlNamespaces.Presentation && root.Name.Namespace != OpenXmlNamespaces.StrictPresentation)
            { throw new InvalidDataException("Invalid presentation part root: " + rootName); }
            return new(name, root, Package.ReadRelationships(name, token));
        }
        internal void CheckSlide(int index)
        {
            _ = Package;
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Slides.Count);
        }
        public void Dispose() { if (_ownsPackage) { _package?.Dispose(); } _package = null; }
    }
}
