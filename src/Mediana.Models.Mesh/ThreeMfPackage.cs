// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.OpenXml;
using System.Xml.Linq;
using System.Xml;

namespace Mediana.Models.Mesh
{
    internal class ThreeMfPackage(OpenXmlPackage package, ThreeMfReaderOptions options)
    {
        internal const string ModelRelationship = "http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel";
        internal const string ThumbnailRelationship = "http://schemas.openxmlformats.org/package/2006/relationships/metadata/thumbnail";

        internal static OpenXmlPackageOptions PackageOptions(ThreeMfReaderOptions options) => new()
        {
            MaximumInputBytes = options.MaxInputBytes,
            MaximumParts = options.MaxArchiveEntries,
            MaximumPartBytes = options.MaxPartBytes,
            MaximumUncompressedBytes = options.MaxUncompressedBytes,
            MaximumXmlDepth = options.MaxXmlDepth,
            MaximumXmlElements = options.MaxXmlElements
        };

        internal string CanonicalPart(string path) => package.CanonicalPart(path);

        internal Task<byte[]> ReadBytesAsync(string path, int maximum, CancellationToken token)
        {
            byte[] bytes = package.ReadPart(path, token, Math.Min(maximum, options.MaxPartBytes));
            if (bytes.Length > maximum) { throw new InvalidDataException("3MF part exceeds its configured byte limit."); }
            return Task.FromResult(bytes);
        }

        internal Task<XDocument> ReadXmlAsync(string path, CancellationToken token)
        {
            if (package.ContentType(path) != ThreeMfReader.ModelContentType) { throw new InvalidDataException("Invalid 3MF model-part content type."); }
            try
            {
                return Task.FromResult(package.ReadXml(path, token, options.MaxPartBytes));
            }
            catch (XmlException exception)
            {
                System.Diagnostics.Trace.TraceError("{0}", exception);
                throw new InvalidDataException("Invalid XML in 3MF model part: " + path, exception);
            }
        }

        internal Task<string?> FindRootRelationshipAsync(string type, bool required, CancellationToken token)
        {
            OpenXmlRelationship[] matches = package.ReadRelationships("", token).Where(item => item.Type == type).ToArray();
            if (matches.Length == 0 && !required) { return Task.FromResult<string?>(null); }
            if (matches.Length != 1 || matches[0].External) { throw new InvalidDataException("3MF requires one internal relationship of the requested type."); }
            return Task.FromResult<string?>(CanonicalPart(OpenXmlPackage.Resolve("", matches[0])));
        }

        internal Task<HashSet<string>> ModelReferencesAsync(string part, CancellationToken token)
        {
            HashSet<string> references = new(StringComparer.OrdinalIgnoreCase);
            foreach (OpenXmlRelationship relationship in package.ReadRelationships(part, token))
            {
                if (relationship.Type != ModelRelationship) { continue; }
                if (relationship.External) { throw new InvalidDataException("External 3MF model relationships are unsupported."); }
                references.Add(CanonicalPart(OpenXmlPackage.Resolve(part, relationship)));
            }
            return Task.FromResult(references);
        }

        internal static string Resolve(string sourcePart, string target) => OpenXmlPackage.Resolve(sourcePart, new("", "", target, false));
    }
}
