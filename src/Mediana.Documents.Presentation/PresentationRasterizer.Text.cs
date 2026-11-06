// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using Mediana.Drawing.Fonts;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Xml.Linq;
namespace Mediana.Documents.Presentation
{
    internal partial class PresentationRasterizer
    {
        private readonly HashSet<(FontRequest Request, TrueTypeFont Font)> _faces = [];
        private void DrawText(XElement body, IReadOnlyList<XElement> chain, double width, double height, Matrix3x2 transform)
        {
            XNamespace a = body.Elements().FirstOrDefault()?.Name.Namespace ?? PresentationXml.Drawing(body);
            XElement bodyPr = new(a + "bodyPr");
            foreach (XElement inherited in chain.Reverse().Select(shape => shape.Elements().FirstOrDefault(item => item.Name.LocalName == "txBody")?.Element(a + "bodyPr"))
                .OfType<XElement>().Append(body.Element(a + "bodyPr")).OfType<XElement>())
            {
                foreach (XAttribute attribute in inherited.Attributes()) { bodyPr.SetAttributeValue(attribute.Name, attribute.Value); }
                foreach (XElement child in inherited.Elements())
                {
                    if (child.Name.LocalName is "noAutofit" or "normAutofit" or "spAutoFit")
                    { bodyPr.Elements().Where(item => item.Name.LocalName is "noAutofit" or "normAutofit" or "spAutoFit").Remove(); }
                    bodyPr.Element(child.Name)?.Remove(); bodyPr.Add(new XElement(child));
                }
            }
            string vertical = (string?)bodyPr.Attribute("vert") ?? "horz";
            if (vertical != "horz") { throw new NotSupportedException("Unsupported presentation text direction: " + vertical); }
            double left = PresentationXml.Points(bodyPr, "lIns", 7.2), right = PresentationXml.Points(bodyPr, "rIns", 7.2);
            double top = PresentationXml.Points(bodyPr, "tIns", 3.6), bottom = PresentationXml.Points(bodyPr, "bIns", 3.6);
            if (width - left - right <= 0 || height - top - bottom <= 0) { return; }
            bool wrap = (string?)bodyPr.Attribute("wrap") switch { null or "square" => true, "none" => false, _ => throw new InvalidDataException("Invalid presentation text wrapping.") };
            double fontScale = PresentationXml.Percent(bodyPr.Element(a + "normAutofit"), "fontScale", 1);
            if (fontScale <= 0 || fontScale > 1) { throw new InvalidDataException("Invalid presentation font scale."); }
            XElement? autoFit = bodyPr.Element(a + "normAutofit");
            double lineReduction = PresentationXml.Percent(autoFit, "lnSpcReduction");
            if (lineReduction < 0 || lineReduction > 1) { throw new InvalidDataException("Invalid presentation line spacing reduction."); }
            List<PresentationTextParagraph> paragraphs = [];
            foreach (XElement paragraph in body.Elements(a + "p"))
            {
                token.ThrowIfCancellationRequested();
                List<XElement> styles = ParagraphStyles(paragraph, body, chain, a);
                double indent = Property(styles, "indent", 0) / PresentationXml.EmuPerPoint;
                double marginLeft = Property(styles, "marL", 0) / PresentationXml.EmuPerPoint, marginRight = Property(styles, "marR", 0) / PresentationXml.EmuPerPoint;
                string alignment = Attribute(styles, "algn") ?? "l";
                List<PresentationTextAtom> atoms = [];
                XElement? bullet = styles.SelectMany(item => item.Elements()).LastOrDefault(item => item.Name.LocalName is "buNone" or "buChar" or "buAutoNum");
                if (bullet?.Name.LocalName == "buAutoNum") { throw new NotSupportedException("Automatic presentation bullet numbering is not supported."); }
                if (bullet?.Name.LocalName == "buChar")
                {
                    string character = (string?)bullet.Attribute("char") ?? throw new InvalidDataException("Missing presentation bullet character.");
                    AddText(atoms, character, RunProperties(styles, null, a), fontScale, a);
                    AddText(atoms, " ", RunProperties(styles, null, a), fontScale, a);
                }
                foreach (XElement run in paragraph.Elements())
                {
                    if (run.Name == a + "r" || run.Name == a + "fld")
                    { AddText(atoms, run.Element(a + "t")?.Value ?? "", RunProperties(styles, run.Element(a + "rPr"), a), fontScale, a); }
                    else if (run.Name == a + "br") { atoms.Add(new(null, 0, null, 0, default, 0, 0, 0, Break: true)); }
                }
                List<XElement> mark = RunProperties(styles, paragraph.Element(a + "endParaRPr"), a);
                double markSize = Property(mark, "sz", 1800) / 100 * fontScale;
                paragraphs.Add(new(atoms, styles, markSize, indent, marginLeft, marginRight, alignment));
            }
            double availableHeight = height - top - bottom;
            double scale = 1;
            (double Height, bool FitsWidth) measured = LayoutText(paragraphs, width, left, right, wrap, lineReduction, scale);
            if (autoFit is not null && (measured.Height > availableHeight || !measured.FitsWidth))
            {
                double low = 0, high = 1;
                while (high - low > 1d / 100000)
                {
                    token.ThrowIfCancellationRequested();
                    double middle = (low + high) / 2;
                    (double Height, bool FitsWidth) candidate = LayoutText(paragraphs, width, left, right, wrap, lineReduction, middle);
                    if (candidate.Height > availableHeight || !candidate.FitsWidth) { high = middle; }
                    else { low = middle; }
                }
                if (low == 0) { throw new NotSupportedException("Presentation paragraph spacing cannot fit within the automatic text bounds."); }
                scale = low;
            }
            List<PresentationTextLine> lines = [];
            double y = top + LayoutText(paragraphs, width, left, right, wrap, lineReduction, scale, lines).Height;
            string anchor = (string?)bodyPr.Attribute("anchor") ?? "t";
            double delta = anchor switch { "t" => 0, "ctr" => (height - bottom - y) / 2, "b" => height - bottom - y, _ => throw new NotSupportedException("Unsupported presentation text anchor: " + anchor) };
            canvas.Save();
            try
            {
                ClipText(bodyPr, width, height, transform);
                foreach (PresentationTextLine line in lines)
                {
                    double x = line.X, baseline = top + line.Y + line.Baseline + delta;
                    foreach (PresentationTextAtom atom in line.Atoms)
                    {
                        token.ThrowIfCancellationRequested();
                        if (atom.Graphic is InlineGraphic graphic)
                        { graphic.Draw(canvas, Matrix3x2.CreateScale((float)(atom.Size * scale)) * Matrix3x2.CreateTranslation((float)x, (float)(baseline - atom.Ascent * scale)) * transform, token); }
                        else if (atom.Font is TrueTypeFont font)
                        {
                            float glyphScale = (float)(atom.Size * scale / font.Units);
                            _text.DrawGlyphAntialiased(canvas, font, atom.Glyph, atom.Color, Matrix3x2.CreateScale(glyphScale, -glyphScale) * Matrix3x2.CreateTranslation((float)x, (float)baseline) * transform, token);
                        }
                        x += atom.Advance * scale + (atom.Space ? line.ExtraSpace : 0);
                    }
                }
            }
            finally { canvas.Restore(); }
        }
        private void AddText(List<PresentationTextAtom> atoms, string value, List<XElement> properties, double fontScale, XNamespace a)
        {
            double size = Property(properties, "sz", 1800) / 100 * fontScale;
            if (size <= 0 || size > 4000) { throw new InvalidDataException("Invalid presentation font size."); }
            string family = properties.Select(item => (string?)item.Element(a + "latin")?.Attribute("typeface")).LastOrDefault(item => item is not null) ?? "+mn-lt";
            FontRequest request = new(scene.Theme.Font(family), Bool(properties, "b"), Bool(properties, "i"));
            XElement? fill = properties.Select(item => item.Element(a + "solidFill")).LastOrDefault(item => item is not null);
            Rgba32 color = fill is null ? new(0, 0, 0) : scene.Theme.Color(fill.Elements().Single());
            TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(value);
            while (elements.MoveNext())
            {
                token.ThrowIfCancellationRequested();
                string element = elements.GetTextElement();
                if (element == "\n") { atoms.Add(new(null, 0, null, 0, default, 0, 0, 0, Break: true)); continue; }
                if (_text.ResolveGraphic(element, token) is InlineGraphic graphic)
                { CountGlyph(); atoms.Add(new(null, 0, graphic, size, color, graphic.Advance * size, graphic.Ascent * size, graphic.Descent * size)); continue; }
                foreach (Rune rune in element.EnumerateRunes())
                {
                    CountGlyph();
                    (TrueTypeFont font, int glyph) = _text.ResolveGlyph(request, rune.Value, token);
                    if (_faces.Add((request, font)) && TextRenderer.DescribeFontSubstitution(request, font) is string warning)
                    { options.WarningHandler?.Invoke(warning); }
                    if (glyph == 0) { throw new NotSupportedException("Presentation font has no glyph for U+" + rune.Value.ToString("X", CultureInfo.InvariantCulture)); }
                    double scale = size / font.Units;
                    atoms.Add(new(font, glyph, null, size, color, font.Advance(glyph) * scale, font.Ascent * scale, Math.Max(0, -font.Descent * scale), Rune.IsWhiteSpace(rune)));
                }
            }
        }
        private void CountGlyph()
        {
            if (++_glyphs > document.Options.MaximumGlyphs) { throw new InvalidDataException("Presentation exceeds MaximumGlyphs."); }
        }
        private List<XElement> ParagraphStyles(XElement paragraph, XElement body, IReadOnlyList<XElement> chain, XNamespace a)
        {
            XElement? ppr = paragraph.Element(a + "pPr");
            int level = checked((int)PresentationXml.Number(ppr, "lvl"));
            if (level is < 0 or > 8) { throw new InvalidDataException("Invalid presentation paragraph level."); }
            string styleName = "lvl" + (level + 1).ToString(CultureInfo.InvariantCulture) + "pPr";
            List<XElement> result = [];
            Add(scene.Defaults);
            string type = (string?)chain.Select(PresentationXml.Placeholder).FirstOrDefault(item => item is not null)?.Attribute("type") ?? "obj";
            string masterStyle = type switch { "title" or "ctrTitle" => "titleStyle", "body" or "subTitle" or "obj" => "bodyStyle", _ => "otherStyle" };
            Add(scene.Master?.Root.Elements().FirstOrDefault(item => item.Name.LocalName == "txStyles")?.Elements().FirstOrDefault(item => item.Name.LocalName == masterStyle));
            foreach (XElement shape in chain.Reverse())
            {
                XElement? fontReference = shape.Elements().FirstOrDefault(item => item.Name.LocalName == "style")?.Element(a + "fontRef");
                if (fontReference is not null)
                {
                    string? family = (string?)fontReference.Attribute("idx") switch
                    {
                        "major" => "+mj-lt",
                        "minor" => "+mn-lt",
                        "none" => null,
                        _ => throw new InvalidDataException("Invalid presentation theme font reference.")
                    };
                    XElement properties = new(a + "defRPr");
                    if (family is not null) { properties.Add(new XElement(a + "latin", new XAttribute("typeface", family))); }
                    if (fontReference.Elements().FirstOrDefault() is XElement color) { properties.Add(new XElement(a + "solidFill", new XElement(color))); }
                    result.Add(new(a + "defPPr", properties));
                }
                Add(shape.Elements().FirstOrDefault(item => item.Name.LocalName == "txBody")?.Element(a + "lstStyle"));
            }
            Add(body.Element(a + "lstStyle"));
            if (ppr is not null) { result.Add(ppr); }
            return result;
            void Add(XElement? list)
            {
                if (list?.Element(a + "defPPr") is XElement defaults) { result.Add(defaults); }
                if (list?.Element(a + styleName) is XElement selected) { result.Add(selected); }
            }
        }
        private static List<XElement> RunProperties(List<XElement> styles, XElement? own, XNamespace a)
        {
            List<XElement> result = styles.Select(item => item.Element(a + "defRPr")).OfType<XElement>().ToList();
            if (own is not null) { result.Add(own); }
            return result;
        }
        private static string? Attribute(List<XElement> values, string name) => values.Select(item => (string?)item.Attribute(name)).LastOrDefault(item => item is not null);
        private static double Property(List<XElement> values, string name, double defaultValue)
        {
            XElement? value = values.LastOrDefault(item => item.Attribute(name) is not null);
            return value is null ? defaultValue : PresentationXml.Number(value, name);
        }
        private static bool Bool(List<XElement> values, string name) => values.LastOrDefault(item => item.Attribute(name) is not null) is XElement value && PresentationXml.Boolean(value, name);
        private static double Spacing(IReadOnlyList<XElement> styles, string name, double natural, double defaultValue = 0)
        {
            XElement? spacing = styles.SelectMany(item => item.Elements()).LastOrDefault(item => item.Name.LocalName == name)?.Elements().FirstOrDefault();
            if (spacing is null) { return defaultValue; }
            return spacing.Name.LocalName switch { "spcPct" => PresentationXml.Percent(spacing, "val") * natural, "spcPts" => PresentationXml.Number(spacing, "val") / 100, _ => throw new InvalidDataException("Invalid presentation paragraph spacing.") };
        }
    }
}
