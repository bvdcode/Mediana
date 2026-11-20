// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.OpenXml;
using System.Xml.Linq;

namespace Mediana.Documents.Word
{
    internal class WordTextMargins(OpenXmlPackage package, string main, DocumentTextOptions options)
    {
        private readonly Dictionary<(DocumentTextSourceKind Kind, DocumentHeaderFooterVariant Variant), string> _parts = [];
        private Dictionary<string, OpenXmlRelationship>? _relationships;

        internal IEnumerable<DocumentTextBlock> Read(XElement? properties, int section, DocumentTextBudget budget, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            foreach (DocumentTextSourceKind kind in new[] { DocumentTextSourceKind.Header, DocumentTextSourceKind.Footer })
            {
                bool include = kind switch
                {
                    DocumentTextSourceKind.Header => options.IncludeHeaders,
                    DocumentTextSourceKind.Footer => options.IncludeFooters,
                    _ => throw new InvalidOperationException("Invalid Word margin kind.")
                };
                if (!include) { continue; }
                string name = kind switch
                {
                    DocumentTextSourceKind.Header => "header",
                    DocumentTextSourceKind.Footer => "footer",
                    _ => throw new InvalidOperationException("Invalid Word margin kind.")
                };
                HashSet<DocumentHeaderFooterVariant> seen = [];
                foreach (XElement reference in properties?.Elements(properties.Name.Namespace + (name + "Reference")) ?? [])
                {
                    token.ThrowIfCancellationRequested();
                    DocumentHeaderFooterVariant variant = ((string?)reference.Attribute(reference.Name.Namespace + "type")) switch
                    {
                        "default" => DocumentHeaderFooterVariant.Default,
                        "first" => DocumentHeaderFooterVariant.First,
                        "even" => DocumentHeaderFooterVariant.Even,
                        _ => throw new InvalidDataException("Invalid Word header/footer type.")
                    };
                    if (!seen.Add(variant)) { throw new InvalidDataException("Duplicate Word header/footer type."); }
                    XNamespace relationships = reference.Name.NamespaceName == "http://purl.oclc.org/ooxml/wordprocessingml/main"
                        ? OpenXmlNamespaces.StrictOfficeRelationships : OpenXmlNamespaces.OfficeRelationships;
                    string id = (string?)reference.Attribute(relationships + "id") ?? throw new InvalidDataException("Missing Word header/footer relationship.");
                    _relationships ??= package.ReadRelationships(main, token).ToDictionary(item => item.Id, StringComparer.Ordinal);
                    if (!_relationships.TryGetValue(id, out OpenXmlRelationship? relation) || !OpenXmlNamespaces.IsOfficeRelationship(relation.Type, name))
                    { throw new InvalidDataException("Invalid Word header/footer relationship."); }
                    _parts[(kind, variant)] = OpenXmlPackage.Resolve(main, relation);
                }
                if (!options.IncludesPart(section)) { continue; }
                foreach (DocumentHeaderFooterVariant variant in new[] { DocumentHeaderFooterVariant.Default, DocumentHeaderFooterVariant.First, DocumentHeaderFooterVariant.Even })
                {
                    token.ThrowIfCancellationRequested();
                    if (!_parts.TryGetValue((kind, variant), out string? part)) { continue; }
                    foreach (DocumentTextBlock block in WordTextReader.ReadMargin(package, package.CanonicalPart(part), section, kind, variant, options, budget, token))
                    { yield return block; }
                }
            }
        }
    }
}
