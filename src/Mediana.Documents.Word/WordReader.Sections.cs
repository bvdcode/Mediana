// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Xml.Linq;

namespace Mediana.Documents.Word
{
    public static partial class WordReader
    {
        private static IReadOnlyList<WordSectionSource> SectionSources(WordReadContext context, XElement body)
        {
            List<WordSectionSource> sections = [];
            List<XElement> elements = [];
            bool terminal = false;
            foreach (XElement element in BodyElements(body, context.W))
            {
                context.Token.ThrowIfCancellationRequested();
                if (terminal) { throw new InvalidDataException("Word section properties must terminate the body."); }
                XElement? properties;
                if (element.Name == context.W + "sectPr") { properties = element; terminal = true; }
                else
                {
                    elements.Add(element);
                    properties = element.Name == context.W + "p" ? element.Element(context.W + "pPr")?.Element(context.W + "sectPr") : null;
                }
                if (properties is null) { continue; }
                sections.Add(new(properties, elements.AsReadOnly()));
                elements = [];
            }
            if (!terminal) { sections.Add(new(new XElement(context.W + "sectPr"), elements.AsReadOnly())); }
            return sections.AsReadOnly();
        }

        private static IEnumerable<XElement> BodyElements(XElement parent, XNamespace word)
        {
            foreach (XElement element in parent.Elements())
            {
                XElement? content = element.Name == word + "sdt" ? element.Element(word + "sdtContent") : element;
                if (element.Name == word + "sdt" || element.Name == word + "customXml" || element.Name == word + "ins")
                {
                    if (content is not null)
                    {
                        foreach (XElement nested in BodyElements(content, word)) { yield return nested; }
                    }
                }
                else { yield return element; }
            }
        }

        private static WordSection ReadSection(WordReadContext context, WordSectionSource source, WordContentReader reader,
            string main, WordSection? previous, bool even)
        {
            XElement properties = source.Properties;
            WordPageGeometry geometry = Geometry(context, properties);
            string? type = context.Attribute(properties.Element(context.W + "type"), "val");
            WordSectionStart start = type switch
            {
                null or "nextPage" => WordSectionStart.NextPage,
                "continuous" => WordSectionStart.Continuous,
                "evenPage" => WordSectionStart.EvenPage,
                "oddPage" => WordSectionStart.OddPage,
                "nextColumn" => throw new NotSupportedException("Column section breaks require multi-column Word layout."),
                _ => throw new InvalidDataException("Invalid Word section break type.")
            };
            return new(reader.Blocks(source.Elements, main), geometry, Margin("header", previous?.Header), Margin("footer", previous?.Footer),
                start, WordReadContext.On(properties.Element(context.W + "titlePg")), even);

            WordMarginContent Margin(string kind, WordMarginContent? inherited)
            {
                Dictionary<string, IReadOnlyList<WordBlock>> contents = new(StringComparer.Ordinal);
                foreach (XElement reference in properties.Elements(context.W + (kind + "Reference")))
                {
                    string variant = context.Attribute(reference, "type") ?? "default";
                    if (variant is not ("default" or "first" or "even") || contents.ContainsKey(variant))
                    {
                        throw new InvalidDataException("Invalid or duplicated Word header/footer type.");
                    }
                    string id = (string?)reference.Attribute(context.R + "id") ?? throw new InvalidDataException("Missing Word header/footer relationship.");
                    string part = context.RelationshipPart(main, id);
                    contents.Add(variant, reader.Blocks(context.Xml(part), part));
                }
                return new(contents.GetValueOrDefault("default") ?? inherited?.Default ?? Array.Empty<WordBlock>(),
                    contents.GetValueOrDefault("first") ?? inherited?.First ?? Array.Empty<WordBlock>(),
                    contents.GetValueOrDefault("even") ?? inherited?.Even ?? Array.Empty<WordBlock>());
            }
        }
    }
}
