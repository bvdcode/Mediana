# Shared text resources

`Mediana.Drawing` owns font selection, font collections, glyph outlines, inline
artwork contracts and canvas drawing. Document modules supply their own text
layout and document-specific character decoding.

```csharp
using Mediana.Drawing;
using Mediana.Drawing.Emoji.Fluent;
using Mediana.Documents.Word;
using Mediana.Documents.Pdf;
using Mediana.Images.Svg;

FontCollection fonts = await FontCollection.LoadAsync(fontFiles);
using FluentEmojiProvider emoji = new();
TextOptions text = new()
{
    FontResolver = fonts.Resolve,
    FallbackFontResolver = fonts.ResolveCharacter,
    InlineGraphicProvider = emoji,
    SubstituteMissingFonts = true
};

WordLayout layout = WordRenderer.Layout(wordDocument, new() { Text = text });
using SvgDocument scene = SvgReader.Read(svgXml, new() { Text = text });
using PdfDocument pdf = await PdfReader.ReadAsync(pdfStream, new() { Text = text });
```

The optional Fluent module depends on the SVG renderer. Drawing and the document
modules do not depend on Fluent; applications select resource providers at composition.
The library performs no implicit font discovery, network access or resource downloads.
The CLI discovers installed fonts once when the first text-rendering operation starts
and shares the resulting resources across Word, SVG and external PDF font resolution.
Metadata probing does not load fonts or emoji resources.

A `FontRequest` contains family, bold and italic attributes. Resolvers return a font
program or empty memory. `TextRenderer` owns its parsed font programs and caches for
one operation; separate operations can use the same `TextOptions` concurrently when
its providers support concurrency. Font-face count, total decoded font bytes, outline
commands and distinct inline graphics have explicit limits. Default limits are
64 faces, 64 MiB of font programs, 2 million outline commands and 256 graphics.

`TextRenderer.ResolveGlyph` resolves Unicode characters to font glyphs. `Outline`
returns an independent mutable path. `DrawGlyph` draws directly into an existing
`RasterCanvas` with the requested color and transform. Format implementations use the
same cached font geometry for their own stroke, fill, clipping and placement rules.
`ResolveGraphic` preserves the complete text element, including joiners, presentation
selectors and modifiers. An absent graphic follows the caller's existing font policy.

Word and SVG support color artwork through this shared provider. Word owns paragraph
layout and pagination; SVG owns text anchors, positioning, paths and scene effects.
PDF retains its encoded character maps, embedded fonts, widths and text modes: its
external font names are mapped to shared family/style requests, while glyph meaning
stays with the PDF. PDF does not replace encoded glyphs with Unicode emoji artwork.
TGS text layers, general complex-script shaping and OpenType color-font tables remain
open capabilities.

SVG positioning indexes Unicode code points independently of grapheme selection,
as specified in [SVG 2 text layout](https://www.w3.org/TR/SVG2/text.html).
