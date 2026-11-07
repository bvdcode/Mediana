// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.OpenXml;
using System.Xml.Linq;
namespace Mediana.Documents.Presentation
{
    internal record PresentationPart(string Name, XElement Root, IReadOnlyList<OpenXmlRelationship> Relationships)
    {
        internal XElement? Tree => Root.Element(Root.Name.Namespace + "cSld")?.Element(Root.Name.Namespace + "spTree");
        internal string Relationship(string id, string type)
        {
            OpenXmlRelationship relationship = Relationships.FirstOrDefault(item => item.Id == id)
                ?? throw new InvalidDataException("Missing presentation relationship: " + id);
            if (!OpenXmlNamespaces.IsOfficeRelationship(relationship.Type, type)) { throw new InvalidDataException("Unexpected presentation relationship type."); }
            return OpenXmlPackage.Resolve(Name, relationship);
        }
    }
}
