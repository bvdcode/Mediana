// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.IO;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace Mediana.Documents.OpenXml
{
    /// <summary>A bounded, read-only OPC package borrowing input memory until disposed.
    /// Instances have a single consumer. External relationships are never opened.</summary>
    public class OpenXmlPackage : IDisposable
    {
        private static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
        private static readonly XNamespace ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
        private readonly ReadOnlyMemoryStream _source;
        private ZipArchive? _archive;
        private readonly OpenXmlPackageOptions _options;
        private readonly Dictionary<string, ZipArchiveEntry> _parts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _overrides = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _extensions = new(StringComparer.OrdinalIgnoreCase);
        private int _xmlElements;
        private long _readBytes;

        private OpenXmlPackage(ReadOnlyMemory<byte> input, OpenXmlPackageOptions options)
        {
            _source = new(input);
            _options = options;
        }

        /// <summary>Opens borrowed memory. Failed initialization releases all package resources.</summary>
        public static OpenXmlPackage Open(ReadOnlyMemory<byte> input, OpenXmlPackageOptions? options = null, CancellationToken cancellationToken = default)
        {
            options ??= new();
            if (options.MaximumInputBytes <= 0 || options.MaximumParts <= 0 || options.MaximumPartBytes <= 0
                || options.MaximumUncompressedBytes <= 0 || options.MaximumXmlDepth <= 0 || options.MaximumXmlElements <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options));
            }
            if (input.Length > options.MaximumInputBytes) { throw new InvalidDataException("OPC input exceeds MaximumInputBytes."); }
            OpenXmlPackage package = new(input, options);
            bool completed = false;
            try
            {
                package.Initialize(cancellationToken);
                completed = true;
                return package;
            }
            finally
            {
                if (!completed) { package.Dispose(); }
            }
        }

        public static IReadOnlyList<string> OfficeDocumentRelationships { get; } = Array.AsReadOnly(new[]
        {
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument",
            "http://purl.oclc.org/ooxml/officeDocument/relationships/officeDocument"
        });

        /// <summary>Gets the main office document part using package relationships.</summary>
        public string MainPart(CancellationToken cancellationToken = default) => MainPart(OfficeDocumentRelationships, cancellationToken);

        /// <summary>Resolves exactly one root relationship from the accepted types, rejecting external or ambiguous roots.</summary>
        public string MainPart(IEnumerable<string> relationshipTypes, CancellationToken cancellationToken = default)
            => MainPartInfo(relationshipTypes, cancellationToken).Name;

        internal (string Name, string RelationshipType) MainPartInfo(IEnumerable<string> relationshipTypes, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(relationshipTypes);
            HashSet<string> accepted = new(relationshipTypes, StringComparer.Ordinal);
            OpenXmlRelationship[] matches = ReadRelationships("", cancellationToken).Where(relation => accepted.Contains(relation.Type)).ToArray();
            if (matches.Length != 1 || matches[0].External) { throw new InvalidDataException("OPC must have one internal main-part relationship of the registered types."); }
            string name = Resolve("", matches[0]);
            if (!_parts.ContainsKey(name)) { throw new InvalidDataException("OPC main part is missing."); }
            return (CanonicalPart(name), matches[0].Type);
        }

        /// <summary>Returns the stored spelling of an equivalent OPC part name.</summary>
        public string CanonicalPart(string name)
        {
            ObjectDisposedException.ThrowIf(_archive is null, this);
            return _parts.TryGetValue(name, out ZipArchiveEntry? part) ? part.FullName
                : throw new InvalidDataException("OPC part is missing: " + name);
        }

        public bool ContainsPart(string name)
        {
            ObjectDisposedException.ThrowIf(_archive is null, this);
            return _parts.ContainsKey(name);
        }

        public string ContentType(string name)
        {
            ObjectDisposedException.ThrowIf(_archive is null, this);
            if (_overrides.TryGetValue(name, out string? content)) { return content; }
            string extension = Path.GetExtension(name).TrimStart('.');
            return _extensions.TryGetValue(extension, out content) ? content : throw new InvalidDataException("OPC part has no declared content type.");
        }

        /// <summary>Reads exactly one bounded part. The cumulative budget includes repeated reads.</summary>
        public byte[] ReadPart(string name, CancellationToken cancellationToken = default, int? maximumBytes = null)
        {
            ObjectDisposedException.ThrowIf(_archive is null, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (!_parts.TryGetValue(name, out ZipArchiveEntry? part)) { throw new InvalidDataException("OPC part is missing: " + name); }
            if (maximumBytes is <= 0) { throw new ArgumentOutOfRangeException(nameof(maximumBytes)); }
            if (maximumBytes is int maximum && part.Length > maximum) { throw new InvalidDataException("OPC part exceeds the requested byte limit."); }
            if (part.Length > _options.MaximumUncompressedBytes - _readBytes) { throw new InvalidDataException("OPC reads exceed MaximumUncompressedBytes."); }
            _readBytes += part.Length;
            byte[] data = new byte[checked((int)part.Length)];
            using Stream stream = part.Open();
            int offset = 0;
            while (offset < data.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = stream.Read(data.AsSpan(offset, Math.Min(8192, data.Length - offset)));
                if (count == 0) { throw new InvalidDataException("Truncated OPC part."); }
                offset += count;
            }
            if (stream.ReadByte() != -1) { throw new InvalidDataException("OPC part exceeds its declared length."); }
            return data;
        }

        /// <summary>Reads bounded XML with DTD and external entity resolution disabled.</summary>
        public XDocument ReadXml(string name, CancellationToken cancellationToken = default, int? maximumBytes = null)
        {
            using XmlReader reader = OpenXmlReader(name, cancellationToken, maximumBytes);
            return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }

        /// <summary>Opens bounded streaming XML. Dispose the reader before its package. Early disposal does not drain the part.
        /// The declared part length is reserved against the cumulative read budget before decompression begins.</summary>
        public XmlReader OpenXmlReader(string name, CancellationToken cancellationToken = default, int? maximumBytes = null)
        {
            ObjectDisposedException.ThrowIf(_archive is null, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (!_parts.TryGetValue(name, out ZipArchiveEntry? part)) { throw new InvalidDataException("OPC part is missing: " + name); }
            if (maximumBytes is <= 0) { throw new ArgumentOutOfRangeException(nameof(maximumBytes)); }
            if (maximumBytes is int maximum && part.Length > maximum) { throw new InvalidDataException("OPC part exceeds the requested byte limit."); }
            if (part.Length > _options.MaximumUncompressedBytes - _readBytes) { throw new InvalidDataException("OPC reads exceed MaximumUncompressedBytes."); }
            _readBytes += part.Length;
            OpenXmlPartStream stream = new(part.Open(), part.Length, cancellationToken);
            bool completed = false;
            try
            {
                XmlReaderSettings settings = new()
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = _options.MaximumPartBytes,
                    CloseInput = true
                };
                XmlReader reader = XmlReader.Create(stream, settings);
                BoundedXmlReader bounded = new(reader, _options.MaximumXmlDepth, ConsumeXmlElement, cancellationToken);
                completed = true;
                return bounded;
            }
            finally { if (!completed) { stream.Dispose(); } }
        }

        private void ConsumeXmlElement()
        {
            if (++_xmlElements > _options.MaximumXmlElements) { throw new InvalidDataException("OPC XML exceeds its element budget."); }
        }

        public IReadOnlyList<OpenXmlRelationship> ReadRelationships(string sourcePart, CancellationToken cancellationToken = default)
        {
            int slash = sourcePart.LastIndexOf('/') + 1;
            string name = sourcePart.Length == 0 ? "_rels/.rels" : sourcePart[..slash] + "_rels/" + sourcePart[slash..] + ".rels";
            if (!ContainsPart(name)) { return Array.Empty<OpenXmlRelationship>(); }
            XElement root = ReadXml(name, cancellationToken).Root ?? throw new InvalidDataException("Empty OPC relationships.");
            if (root.Name != Relationships + "Relationships") { throw new InvalidDataException("Invalid OPC relationships root."); }
            List<OpenXmlRelationship> result = [];
            HashSet<string> ids = new(StringComparer.Ordinal);
            foreach (XElement element in root.Elements(Relationships + "Relationship"))
            {
                string id = Required(element, "Id"), type = Required(element, "Type"), target = Required(element, "Target");
                bool external = (string?)element.Attribute("TargetMode") switch
                {
                    null or "Internal" => false,
                    "External" => true,
                    _ => throw new InvalidDataException("Invalid OPC TargetMode.")
                };
                if (!ids.Add(id)) { throw new InvalidDataException("Duplicate OPC relationship identifier."); }
                result.Add(new(id, type, target, external));
            }
            return result.AsReadOnly();
        }

        public static string Resolve(string sourcePart, OpenXmlRelationship relationship)
        {
            ArgumentNullException.ThrowIfNull(relationship);
            if (relationship.External) { throw new NotSupportedException("External OPC relationships are not opened."); }
            return OpenXmlPartName.Resolve(sourcePart, relationship.Target);
        }

        public void Dispose()
        {
            _archive?.Dispose();
            _archive = null;
            _source.Dispose();
        }

        private void Initialize(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            _archive = new(_source, ZipArchiveMode.Read, leaveOpen: true);
            if (_archive.Entries.Count > _options.MaximumParts) { throw new InvalidDataException("OPC exceeds MaximumParts."); }
            long total = 0;
            foreach (ZipArchiveEntry entry in _archive.Entries)
            {
                token.ThrowIfCancellationRequested();
                if (entry.FullName.EndsWith('/')) { continue; }
                string name = OpenXmlPartName.Resolve("", "/" + entry.FullName);
                if (name != entry.FullName || !_parts.TryAdd(name, entry)) { throw new InvalidDataException("Duplicate or noncanonical OPC part name."); }
                if (entry.Length > _options.MaximumPartBytes || entry.Length > _options.MaximumUncompressedBytes - total)
                {
                    throw new InvalidDataException("OPC exceeds its uncompressed part budget.");
                }
                total += entry.Length;
            }
            XElement types = ReadXml("[Content_Types].xml", token).Root ?? throw new InvalidDataException("Empty OPC content types.");
            if (types.Name != ContentTypes + "Types") { throw new InvalidDataException("Invalid OPC content types root."); }
            foreach (XElement element in types.Elements())
            {
                if (element.Name == ContentTypes + "Override")
                {
                    string name = OpenXmlPartName.Resolve("", Required(element, "PartName"));
                    if (!_overrides.TryAdd(name, Required(element, "ContentType"))) { throw new InvalidDataException("Duplicate OPC content type override."); }
                }
                else if (element.Name == ContentTypes + "Default")
                {
                    if (!_extensions.TryAdd(Required(element, "Extension"), Required(element, "ContentType"))) { throw new InvalidDataException("Duplicate OPC default content type."); }
                }
                else { throw new InvalidDataException("Invalid OPC content type declaration."); }
            }
        }

        private static string Required(XElement element, string attribute) => element.Attribute(attribute)?.Value is { Length: > 0 } value
            ? value : throw new InvalidDataException("OPC attribute is required: " + attribute);
    }
}
