// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.OpenXml;
using System.Globalization;
using System.Xml.Linq;
namespace Mediana.Documents.Presentation
{
    internal static class PresentationXml
    {
        internal const double EmuPerPoint = 12700;
        internal static double Number(XElement? element, string attribute, double defaultValue = 0)
        {
            string? text = (string?)element?.Attribute(attribute);
            if (text is null) { return defaultValue; }
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value)
                ? value : throw new InvalidDataException("Invalid DrawingML number: " + attribute);
        }
        internal static double Points(XElement? element, string attribute, double defaultValue = 0) => Number(element, attribute, defaultValue * EmuPerPoint) / EmuPerPoint;
        internal static bool Boolean(XElement? element, string attribute, bool defaultValue = false) => (string?)element?.Attribute(attribute) switch
        {
            null => defaultValue, "1" or "true" => true, "0" or "false" => false, _ => throw new InvalidDataException("Invalid DrawingML boolean.")
        };
        internal static string RelationId(XElement element, string name) => (string?)element.Attribute(XName.Get(name, OpenXmlNamespaces.OfficeRelationships))
            ?? (string?)element.Attribute(XName.Get(name, OpenXmlNamespaces.StrictOfficeRelationships)) ?? throw new InvalidDataException("Missing DrawingML relationship identifier.");
        internal static XNamespace Drawing(XElement element) => element.Name.Namespace == OpenXmlNamespaces.StrictPresentation
            ? OpenXmlNamespaces.StrictDrawing : OpenXmlNamespaces.Drawing;
        internal static XElement? Properties(XElement shape) => shape.Element(shape.Name.Namespace + "spPr");
        internal static XElement? Placeholder(XElement shape) => shape.Elements().FirstOrDefault(item => item.Name.LocalName.StartsWith("nv", StringComparison.Ordinal))?
            .Element(shape.Name.Namespace + "nvPr")?.Element(shape.Name.Namespace + "ph");
    }
}
