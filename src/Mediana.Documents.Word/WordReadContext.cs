// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.OpenXml;
using System.Globalization;
using System.Xml.Linq;

namespace Mediana.Documents.Word
{
    internal class WordReadContext(OpenXmlPackage package, WordReaderOptions options, XNamespace word, CancellationToken token)
    {
        private const string TransitionalRelations = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
        private const string StrictRelations = "http://purl.oclc.org/ooxml/officeDocument/relationships/";
        private readonly Dictionary<string, IReadOnlyList<OpenXmlRelationship>> _relationships = new(StringComparer.Ordinal);
        private readonly HashSet<string> _warnings = new(StringComparer.Ordinal);
        private readonly Dictionary<string, byte[]> _images = new(StringComparer.Ordinal);
        private int _characters;
        private int _blocks;

        internal XNamespace W { get; } = word;
        internal XNamespace R { get; } = word.NamespaceName switch
        {
            "http://schemas.openxmlformats.org/wordprocessingml/2006/main" => TransitionalRelations.TrimEnd('/'),
            "http://purl.oclc.org/ooxml/wordprocessingml/main" => StrictRelations.TrimEnd('/'),
            _ => throw new NotSupportedException("Unsupported WordprocessingML namespace.")
        };
        internal WordReaderOptions Options => options;
        internal CancellationToken Token => token;
        internal IReadOnlyList<string> Warnings => Array.AsReadOnly(_warnings.ToArray());
        internal void Warn(string message) => _warnings.Add(message);
        internal string? Attribute(XElement? element, string name) => (string?)element?.Attribute(W + name);
        internal string? Value(XElement? element) => Attribute(element, "val");
        internal double Length(string value, OpenXmlNumericUnit unit = OpenXmlNumericUnit.Twip) =>
            OpenXmlMeasurement.ToPoints(value, unit, options.AllowFractionalMeasurements, Warn);
        internal double Length(XElement? element, string name, double defaultValue = 0, OpenXmlNumericUnit unit = OpenXmlNumericUnit.Twip) =>
            Attribute(element, name) is string value ? Length(value, unit) : defaultValue;

        internal static bool On(XElement? element)
        {
            if (element is null) { return false; }
            return (string?)element.Attribute(element.Name.Namespace + "val") switch
            {
                null or "1" or "true" or "on" => true,
                "0" or "false" or "off" => false,
                _ => throw new InvalidDataException("Invalid WordprocessingML boolean.")
            };
        }

        internal static int Integer(string value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? result : throw new InvalidDataException("Invalid WordprocessingML integer.");

        internal IReadOnlyList<OpenXmlRelationship> Relationships(string part)
        {
            if (!_relationships.TryGetValue(part, out IReadOnlyList<OpenXmlRelationship>? items))
            {
                items = package.ReadRelationships(part, token);
                _relationships.Add(part, items);
            }
            return items;
        }

        internal string RelationshipPart(string source, string id)
        {
            OpenXmlRelationship relation = Relationships(source).SingleOrDefault(item => item.Id == id)
                ?? throw new InvalidDataException("WordprocessingML relationship is missing.");
            return OpenXmlPackage.Resolve(source, relation);
        }

        internal XElement? RelatedXml(string source, string kind)
        {
            OpenXmlRelationship[] matches = Relationships(source).Where(item => item.Type == TransitionalRelations + kind
                || item.Type == StrictRelations + kind).ToArray();
            if (matches.Length > 1) { throw new InvalidDataException("Duplicate WordprocessingML part relationship."); }
            return matches.Length == 0 ? null : package.ReadXml(OpenXmlPackage.Resolve(source, matches[0]), token).Root;
        }

        internal XElement Xml(string part) => package.ReadXml(part, token).Root ?? throw new InvalidDataException("Empty WordprocessingML part.");

        internal ReadOnlyMemory<byte> Image(string part)
        {
            if (!_images.TryGetValue(part, out byte[]? bytes))
            {
                bytes = package.ReadPart(part, token);
                _images.Add(part, bytes);
            }
            return bytes;
        }

        internal void CountText(int length)
        {
            token.ThrowIfCancellationRequested();
            if (length > options.MaximumCharacters - _characters) { throw new InvalidDataException("Word document exceeds MaximumCharacters."); }
            _characters += length;
        }

        internal void CountBlock()
        {
            token.ThrowIfCancellationRequested();
            if (++_blocks > options.MaximumBlocks) { throw new InvalidDataException("Word document exceeds MaximumBlocks."); }
        }
    }
}
