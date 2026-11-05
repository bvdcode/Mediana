// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.OpenXml;
using System.Xml.Linq;

namespace Mediana.Documents.Word
{
    internal partial class WordContentReader(WordReadContext context, WordStyles styles, WordNumbering numbering)
    {
        private readonly WordFormatReader _formats = new(context);
        private XNamespace W => context.W;

        internal IReadOnlyList<WordBlock> Blocks(XElement parent, string part)
        {
            List<WordBlock> blocks = [];
            foreach (XElement element in parent.Elements())
            {
                context.Token.ThrowIfCancellationRequested();
                if (element.Name.Namespace != W) { throw new NotSupportedException("Unsupported Word block namespace: " + element.Name.NamespaceName); }
                switch (element.Name.LocalName)
                {
                    case "p": context.CountBlock(); blocks.Add(Paragraph(element, part)); break;
                    case "tbl": context.CountBlock(); blocks.Add(Table(element, part)); break;
                    case "sdt":
                        if (element.Element(W + "sdtContent") is XElement content) { blocks.AddRange(Blocks(content, part)); }
                        break;
                    case "customXml" or "ins": blocks.AddRange(Blocks(element, part)); break;
                    case "sectPr" or "tcPr" or "bookmarkStart" or "bookmarkEnd" or "proofErr" or "permStart" or "permEnd": break;
                    default: throw new NotSupportedException("Unsupported Word block: " + element.Name.LocalName);
                }
            }
            return blocks.AsReadOnly();
        }

        private WordParagraph Paragraph(XElement element, string part)
        {
            (XElement properties, WordRunFormat run) = styles.Paragraph(element.Element(W + "pPr"));
            List<WordInline> items = new(numbering.Prefix(properties, run));
            ReadInlines(element, part, run, items);
            WordRunFormat mark = _formats.Run(run, properties.Element(W + "rPr"), false);
            return new(items.AsReadOnly(), _formats.Paragraph(properties), mark);
        }

        private void ReadInlines(XElement parent, string part, WordRunFormat format, List<WordInline> items)
        {
            foreach (XElement element in parent.Elements())
            {
                context.Token.ThrowIfCancellationRequested();
                if (element.Name.Namespace != W) { throw new NotSupportedException("Unsupported Word inline namespace: " + element.Name.NamespaceName); }
                switch (element.Name.LocalName)
                {
                    case "r": ReadRun(element, part, styles.Run(format, element.Element(W + "rPr")), items); break;
                    case "hyperlink" or "fldSimple" or "smartTag" or "customXml" or "ins": ReadInlines(element, part, format, items); break;
                    case "sdt":
                        if (element.Element(W + "sdtContent") is XElement content) { ReadInlines(content, part, format, items); }
                        break;
                    case "pPr" or "bookmarkStart" or "bookmarkEnd" or "proofErr" or "permStart" or "permEnd" or "commentRangeStart" or "commentRangeEnd": break;
                    default: throw new NotSupportedException("Unsupported Word inline: " + element.Name.LocalName);
                }
            }
        }

        private void ReadRun(XElement run, string part, WordRunFormat format, List<WordInline> items)
        {
            foreach (XElement item in run.Elements())
            {
                context.Token.ThrowIfCancellationRequested();
                if (item.Name.Namespace != W) { throw new NotSupportedException("Unsupported Word run namespace: " + item.Name.NamespaceName); }
                switch (item.Name.LocalName)
                {
                    case "t": context.CountText(item.Value.Length); items.Add(new WordText(item.Value, format)); break;
                    case "tab": items.Add(new WordTab(format)); break;
                    case "cr": items.Add(new WordBreak(false, format)); break;
                    case "br":
                        bool page = context.Attribute(item, "type") switch
                        {
                            null or "textWrapping" => false,
                            "page" => true,
                            _ => throw new NotSupportedException("Word column breaks are not supported.")
                        };
                        items.Add(new WordBreak(page, format));
                        break;
                    case "drawing": items.Add(Picture(item, part, format)); break;
                    case "noBreakHyphen": items.Add(new WordText("\u2011", format)); break;
                    case "softHyphen": items.Add(new WordText("\u00ad", format)); break;
                    case "rPr" or "lastRenderedPageBreak" or "instrText" or "fldChar" or "annotationRef" or "commentReference": break;
                    default: throw new NotSupportedException("Unsupported Word run content: " + item.Name.LocalName);
                }
            }
        }

        private WordPicture Picture(XElement drawing, string part, WordRunFormat format)
        {
            XElement inline = drawing.Elements().SingleOrDefault(element => element.Name.LocalName == "inline")
                ?? throw new NotSupportedException("Only inline Word pictures are supported.");
            XElement extent = inline.Elements().Single(element => element.Name.LocalName == "extent");
            XElement blip = inline.Descendants().Single(element => element.Name.LocalName == "blip");
            string id = (string?)blip.Attribute(context.R + "embed") ?? throw new NotSupportedException("Linked Word pictures are not opened.");
            if (inline.Descendants().Any(element => element.Name.LocalName == "srcRect" && element.Attributes().Any(attribute => attribute.Value != "0")))
            {
                throw new NotSupportedException("Cropped Word pictures are not supported.");
            }
            foreach (XElement transform in inline.Descendants().Where(element => element.Name.LocalName == "xfrm"))
            {
                if (transform.Attributes().Any(attribute => attribute.Name.LocalName is "rot" or "flipH" or "flipV" && attribute.Value is not ("0" or "false")))
                {
                    throw new NotSupportedException("Rotated or reflected Word pictures are not supported.");
                }
            }
            string name = context.RelationshipPart(part, id);
            double width = WordFormatReader.Positive(context.Length((string?)extent.Attribute("cx") ?? "0", OpenXmlNumericUnit.EnglishMetricUnit));
            double height = WordFormatReader.Positive(context.Length((string?)extent.Attribute("cy") ?? "0", OpenXmlNumericUnit.EnglishMetricUnit));
            return new(name, context.Image(name), width, height, format);
        }
    }
}
