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
        private readonly Dictionary<string, PresentationPart> _sharedParts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, PresentationPart> _themes = new(StringComparer.Ordinal);
        private readonly XElement? _slideSize;
        private (double Width, double Height)? _dimensions;
        internal PresentationReaderOptions Options { get; }
        internal XElement Defaults { get; }
        /// <summary>Slide width in points. Requires valid declared slide dimensions.</summary>
        /// <exception cref="InvalidDataException">Slide dimensions are missing or invalid.</exception>
        public double Width => Dimensions.Width;
        /// <summary>Slide height in points. Requires valid declared slide dimensions.</summary>
        /// <exception cref="InvalidDataException">Slide dimensions are missing or invalid.</exception>
        public double Height => Dimensions.Height;
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
            _slideSize = root.Element(p + "sldSz") is XElement size ? new XElement(size) : null;
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
        private (double Width, double Height) Dimensions
        {
            get
            {
                if (_dimensions is { } dimensions) { return dimensions; }
                XElement size = _slideSize ?? throw new InvalidDataException("Presentation slide size is missing.");
                double width = PresentationXml.Points(size, "cx"), height = PresentationXml.Points(size, "cy");
                if (width <= 0 || height <= 0) { throw new InvalidDataException("Invalid presentation slide size."); }
                dimensions = (width, height);
                _dimensions = dimensions;
                return dimensions;
            }
        }
        internal PresentationPart ReadPart(string name, string rootName, CancellationToken token)
        {
            _ = Package;
            if (rootName != "sld" && _sharedParts.TryGetValue(name, out PresentationPart? cached)) { return cached; }
            XElement root = Package.ReadXml(name, token).Root ?? throw new InvalidDataException("Empty presentation part.");
            if (root.Name.LocalName != rootName || root.Name.Namespace != OpenXmlNamespaces.Presentation && root.Name.Namespace != OpenXmlNamespaces.StrictPresentation)
            { throw new InvalidDataException("Invalid presentation part root: " + rootName); }
            PresentationPart result = new(name, root, Package.ReadRelationships(name, token));
            if (rootName != "sld") { _sharedParts.Add(name, result); }
            return result;
        }
        internal PresentationPart ReadTheme(string name, CancellationToken token)
        {
            _ = Package;
            token.ThrowIfCancellationRequested();
            if (_themes.TryGetValue(name, out PresentationPart? cached)) { return cached; }
            XElement root = Package.ReadXml(name, token).Root ?? throw new InvalidDataException("Empty presentation theme.");
            if (root.Name.LocalName != "theme" || root.Name.Namespace != OpenXmlNamespaces.Drawing && root.Name.Namespace != OpenXmlNamespaces.StrictDrawing)
            { throw new InvalidDataException("Invalid presentation theme root."); }
            PresentationPart result = new(name, root, Package.ReadRelationships(name, token));
            _themes.Add(name, result);
            return result;
        }
        internal void CheckSlide(int index)
        {
            _ = Package;
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Slides.Count);
        }
        public void Dispose() { if (_ownsPackage) { _package?.Dispose(); } _package = null; _sharedParts.Clear(); _themes.Clear(); }
    }
}
