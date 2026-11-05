// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Xml;

namespace Mediana.Documents.OpenXml
{
    internal class BoundedXmlReader(XmlReader reader, int maximumDepth, Action consumeElement, CancellationToken cancellationToken) : XmlReader
    {
        public override bool Read()
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool found = reader.Read();
            if (!found) { return false; }
            if (reader.Depth > maximumDepth) { throw new InvalidDataException("OPC XML exceeds its depth budget."); }
            if (reader.NodeType == XmlNodeType.Element) { consumeElement(); }
            return true;
        }

        public override int AttributeCount => reader.AttributeCount;
        public override string BaseURI => reader.BaseURI;
        public override int Depth => reader.Depth;
        public override bool EOF => reader.EOF;
        public override bool HasValue => reader.HasValue;
        public override bool IsEmptyElement => reader.IsEmptyElement;
        public override string LocalName => reader.LocalName;
        public override string Name => reader.Name;
        public override string NamespaceURI => reader.NamespaceURI;
        public override XmlNameTable NameTable => reader.NameTable;
        public override XmlNodeType NodeType => reader.NodeType;
        public override string Prefix => reader.Prefix;
        public override ReadState ReadState => reader.ReadState;
        public override string Value => reader.Value;
        public override string XmlLang => reader.XmlLang;
        public override XmlSpace XmlSpace => reader.XmlSpace;
        public override XmlReaderSettings? Settings => reader.Settings;
        public override string GetAttribute(int i) => reader.GetAttribute(i);
        public override string? GetAttribute(string name) => reader.GetAttribute(name);
        public override string? GetAttribute(string name, string? namespaceURI) => reader.GetAttribute(name, namespaceURI);
        public override string? LookupNamespace(string prefix) => reader.LookupNamespace(prefix);
        public override bool MoveToAttribute(string name) => reader.MoveToAttribute(name);
        public override bool MoveToAttribute(string name, string? ns) => reader.MoveToAttribute(name, ns);
        public override void MoveToAttribute(int i) => reader.MoveToAttribute(i);
        public override bool MoveToElement() => reader.MoveToElement();
        public override bool MoveToFirstAttribute() => reader.MoveToFirstAttribute();
        public override bool MoveToNextAttribute() => reader.MoveToNextAttribute();
        public override bool ReadAttributeValue() => reader.ReadAttributeValue();
        public override void ResolveEntity() => reader.ResolveEntity();
        public override void Close() => reader.Close();
    }
}
