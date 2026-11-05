// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Xml.Linq;

namespace Mediana.Documents.Word
{
    internal class WordStyles
    {
        private readonly WordReadContext _context;
        private readonly WordFormatReader _formats;
        private readonly Dictionary<string, XElement> _styles;
        private readonly XElement? _defaults;
        private readonly string? _defaultParagraph;
        private XNamespace W => _context.W;

        internal WordStyles(WordReadContext context, string main)
        {
            _context = context;
            _formats = new(context);
            XElement? root = context.RelatedXml(main, "styles");
            _styles = new(StringComparer.Ordinal);
            XElement[] definitions = root?.Elements(W + "style").ToArray() ?? [];
            HashSet<string> used = definitions.Select(style => context.Attribute(style, "styleId")).OfType<string>().ToHashSet(StringComparer.Ordinal);
            int reassigned = 0;
            foreach (XElement style in definitions)
            {
                string? id = context.Attribute(style, "styleId");
                if (id is null || _styles.ContainsKey(id))
                {
                    do { id = "style-" + (++reassigned).ToString(System.Globalization.CultureInfo.InvariantCulture); } while (!used.Add(id));
                    context.Warn("Word style identifiers were assigned according to ECMA-376; the first definition retains a duplicated identifier.");
                }
                _styles.Add(id, style);
                if (context.Attribute(style, "type") is null or "paragraph" && context.Attribute(style, "default") is "1" or "true" or "on")
                {
                    _defaultParagraph = id;
                }
            }
            _defaults = root?.Element(W + "docDefaults");
        }

        internal (XElement Properties, WordRunFormat Run) Paragraph(XElement? direct)
        {
            XElement properties = new(W + "pPr");
            Merge(properties, _defaults?.Element(W + "pPrDefault")?.Element(W + "pPr"));
            WordRunFormat run = _formats.Run(new(), _defaults?.Element(W + "rPrDefault")?.Element(W + "rPr"), false);
            foreach (XElement style in Chain(_context.Value(direct?.Element(W + "pStyle")) ?? _defaultParagraph, "paragraph"))
            {
                Merge(properties, style.Element(W + "pPr"));
                run = _formats.Run(run, style.Element(W + "rPr"), true);
            }
            Merge(properties, direct);
            return (properties, run);
        }

        internal WordRunFormat Run(WordRunFormat paragraph, XElement? direct)
        {
            WordRunFormat result = paragraph;
            foreach (XElement style in Chain(_context.Value(direct?.Element(W + "rStyle")), "character"))
            {
                result = _formats.Run(result, style.Element(W + "rPr"), true);
            }
            return _formats.Run(result, direct, false);
        }

        private IEnumerable<XElement> Chain(string? id, string type)
        {
            List<XElement> chain = [];
            HashSet<string> seen = new(StringComparer.Ordinal);
            while (id is not null)
            {
                if (!seen.Add(id)) { throw new InvalidDataException("Cyclic Word style inheritance."); }
                if (!_styles.TryGetValue(id, out XElement? style))
                {
                    _context.Warn("An undefined Word style reference was ignored according to ECMA-376.");
                    break;
                }
                if ((_context.Attribute(style, "type") ?? "paragraph") != type) { break; }
                chain.Add(style);
                id = _context.Value(style.Element(W + "basedOn"));
            }
            chain.Reverse();
            return chain;
        }

        internal static void Merge(XElement destination, XElement? source)
        {
            if (source is null) { return; }
            foreach (XAttribute attribute in source.Attributes()) { destination.SetAttributeValue(attribute.Name, attribute.Value); }
            foreach (XElement item in source.Elements())
            {
                XElement? existing = destination.Element(item.Name);
                if (existing is null) { destination.Add(new XElement(item)); }
                else { Merge(existing, item); }
            }
        }
    }
}
