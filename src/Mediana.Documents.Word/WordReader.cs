// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.OpenXml;
using System.Xml.Linq;

namespace Mediana.Documents.Word
{
    /// <summary>Reads a DOCX package through OPC relationships without invoking Office or a native renderer.</summary>
    public static class WordReader
    {
        public const string DocumentContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";

        public static WordDocument Read(ReadOnlyMemory<byte> input, WordReaderOptions? options = null, CancellationToken cancellationToken = default)
        {
            options ??= new();
            if (options.MaximumCharacters <= 0 || options.MaximumBlocks <= 0) { throw new ArgumentOutOfRangeException(nameof(options)); }
            using OpenXmlPackage package = OpenXmlPackage.Open(input, options.Package, cancellationToken);
            string main = package.MainPart(cancellationToken);
            if (package.ContentType(main) != DocumentContentType) { throw new NotSupportedException("The Open XML package is not a DOCX document."); }
            XElement root = package.ReadXml(main, cancellationToken).Root ?? throw new InvalidDataException("Empty Word document.");
            WordReadContext context = new(package, options, root.Name.Namespace, cancellationToken);
            if (root.Name.LocalName != "document") { throw new InvalidDataException("Invalid Word document root."); }
            XElement body = root.Element(context.W + "body") ?? throw new InvalidDataException("Word document has no body.");
            XElement[] sections = body.Descendants(context.W + "sectPr").ToArray();
            if (sections.Length != 1) { throw new NotSupportedException("Word rendering currently requires one page section."); }
            WordPageGeometry geometry = Geometry(context, sections[0]);
            WordStyles styles = new(context, main);
            WordNumbering numbering = new(context, main);
            WordContentReader reader = new(context, styles, numbering);
            IReadOnlyList<WordBlock> blocks = reader.Blocks(body, main);
            WordMarginContent header = ReadMargin("header"), footer = ReadMargin("footer");
            bool first = WordReadContext.On(sections[0].Element(context.W + "titlePg"));
            bool even = WordReadContext.On(context.RelatedXml(main, "settings")?.Element(context.W + "evenAndOddHeaders"));
            return new(blocks, geometry, header, footer, first, even, context.Warnings);

            WordMarginContent ReadMargin(string kind)
            {
                XElement[] references = sections[0].Elements(context.W + (kind + "Reference")).ToArray();
                Dictionary<string, IReadOnlyList<WordBlock>> contents = new(StringComparer.Ordinal);
                foreach (XElement reference in references)
                {
                    string type = context.Attribute(reference, "type") ?? "default";
                    if (type is not ("default" or "first" or "even") || contents.ContainsKey(type)) { throw new InvalidDataException("Invalid or duplicated Word header/footer type."); }
                    string id = (string?)reference.Attribute(context.R + "id") ?? throw new InvalidDataException("Missing Word header/footer relationship.");
                    string part = context.RelationshipPart(main, id);
                    contents.Add(type, reader.Blocks(context.Xml(part), part));
                }
                return new(contents.GetValueOrDefault("default") ?? Array.Empty<WordBlock>(), contents.GetValueOrDefault("first") ?? Array.Empty<WordBlock>(),
                    contents.GetValueOrDefault("even") ?? Array.Empty<WordBlock>());
            }
        }

        private static WordPageGeometry Geometry(WordReadContext context, XElement section)
        {
            XElement? size = section.Element(context.W + "pgSz"), margins = section.Element(context.W + "pgMar");
            WordPageSize? defaults = context.Options.DefaultPageSize;
            if (size is null && defaults is null) { throw new InvalidDataException("Word page size is missing; configure DefaultPageSize."); }
            if (size is null) { context.Warn("Word page size is missing; the configured default page size is used."); }
            double width = WordFormatReader.Positive(context.Length(size, "w", defaults?.Width ?? 0));
            double height = WordFormatReader.Positive(context.Length(size, "h", defaults?.Height ?? 0));
            WordPageGeometry result = new(width, height, context.Length(margins, "left"), context.Length(margins, "right"),
                context.Length(margins, "top"), context.Length(margins, "bottom"), context.Length(margins, "header"), context.Length(margins, "footer"));
            if (result.Left < 0 || result.Right < 0 || result.Left + result.Right >= width || result.Top + result.Bottom >= height)
            {
                throw new InvalidDataException("Word page margins leave no printable body.");
            }
            if (section.Element(context.W + "cols") is XElement cols && WordReadContext.Integer(context.Attribute(cols, "num") ?? "1") != 1)
            {
                throw new NotSupportedException("Multi-column Word sections are not supported.");
            }
            return result;
        }
    }
}
