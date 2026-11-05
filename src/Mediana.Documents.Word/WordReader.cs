// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.OpenXml;
using System.Xml.Linq;

namespace Mediana.Documents.Word
{
    /// <summary>Reads a DOCX package through OPC relationships without invoking Office or a native renderer.</summary>
    public static partial class WordReader
    {
        public const string DocumentContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";

        public static WordDocument Read(ReadOnlyMemory<byte> input, WordReaderOptions? options = null, CancellationToken cancellationToken = default)
        {
            options ??= new();
            using OpenXmlPackage package = OpenXmlPackage.Open(input, options.Package, cancellationToken);
            return Read(package, package.MainPart(cancellationToken), options, cancellationToken);
        }

        internal static WordDocument Read(OpenXmlPackage package, string main, WordReaderOptions options, CancellationToken cancellationToken)
        {
            (WordReadContext context, XElement body) = Structure(package, main, options, cancellationToken);
            WordStyles styles = new(context, main);
            WordNumbering numbering = new(context, main);
            WordContentReader reader = new(context, styles, numbering);
            bool even = WordReadContext.On(context.RelatedXml(main, "settings")?.Element(context.W + "evenAndOddHeaders"));
            List<WordSection> sections = [];
            foreach (WordSectionSource source in SectionSources(context, body))
            {
                sections.Add(ReadSection(context, source, reader, main, sections.LastOrDefault(), even));
            }
            return new(sections.AsReadOnly(), context.Warnings);
        }

        internal static WordPageSize ReadPageSize(OpenXmlPackage package, string main, WordReaderOptions options, CancellationToken token)
        {
            (WordReadContext context, XElement body) = Structure(package, main, options, token);
            WordPageGeometry geometry = Geometry(context, SectionSources(context, body)[0].Properties);
            return new(geometry.Width, geometry.Height);
        }

        private static (WordReadContext Context, XElement Body) Structure(OpenXmlPackage package, string main,
            WordReaderOptions options, CancellationToken token)
        {
            if (options.MaximumCharacters <= 0 || options.MaximumBlocks <= 0) { throw new ArgumentOutOfRangeException(nameof(options)); }
            if (package.ContentType(main) != DocumentContentType) { throw new NotSupportedException("The Open XML package is not a DOCX document."); }
            XElement root = package.ReadXml(main, token).Root ?? throw new InvalidDataException("Empty Word document.");
            WordReadContext context = new(package, options, root.Name.Namespace, token);
            if (root.Name.LocalName != "document") { throw new InvalidDataException("Invalid Word document root."); }
            XElement body = root.Element(context.W + "body") ?? throw new InvalidDataException("Word document has no body.");
            return (context, body);
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
