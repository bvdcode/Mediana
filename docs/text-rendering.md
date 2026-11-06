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
    FontProgramResolver = fonts.ResolveProgram,
    FallbackFontProgramResolver = fonts.ResolveCharacterProgram,
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

A `FontRequest` contains family, bold and italic attributes. Resolvers return a shared
immutable `FontProgram` or font bytes. Parse a program once and return it through
`FontProgramResolver` to reuse decoded font data across previews. Each `TextRenderer`
owns its mutable font readers and caches and has one consumer. Separate operations
can share programs and the same `TextOptions` concurrently when its providers support
concurrency. Font-face count, total decoded font bytes, outline
commands and distinct inline graphics have explicit limits. Default limits are
64 faces, 64 MiB of font programs, 2 million outline commands and 256 graphics.

`TextRenderer.ResolveGlyph` resolves Unicode characters to font glyphs. `Outline`
returns an independent mutable path. `DrawGlyph` draws directly into an existing
`RasterCanvas` with the requested color and transform. Format implementations use the
same cached font geometry for their own stroke, fill, clipping and placement rules.
`ResolveGraphic` preserves the complete text element, including joiners, presentation
selectors and modifiers. An absent graphic follows the caller's existing font policy.

## Drawing at the output resolution

`DrawGlyphAntialiased`, `RasterCanvas.FillPathAntialiased` and
`RasterCanvas.StrokePathAntialiased` integrate fractional pixel coverage at the canvas
resolution. Use these methods when an application needs smooth text or vector edges
without allocating a larger supersampled canvas. `DrawGlyph` and `FillPath` retain
their existing binary coverage; switching to the antialiased methods is explicit.

```csharp
FontProgram program = new(await File.ReadAllBytesAsync(fontFile));
TextOptions options = new() { FontProgramResolver = _ => program };

using PixelBuffer pixels = new(640, 480);
RasterCanvas canvas = new(pixels);
TextRenderer renderer = new(options);
TrueTypeFont face = renderer.ResolveFont(new("Preview"))!;
float scale = 24f / face.Units;
renderer.DrawGlyphAntialiased(canvas, face, face.Glyph('A'),
    new Rgba32(20, 30, 40, 255),
    Matrix3x2.CreateScale(scale, -scale) * Matrix3x2.CreateTranslation(12, 36));
```

This example uses `Mediana.Drawing.Fonts` and `System.Numerics`. Font coordinates
have an upward Y axis; the negative vertical scale places the baseline at canvas Y=36.
Document layout still supplies glyph order, advances, kerning and line breaks.

Antialiased glyph placement is rounded to 1/256 pixel. A renderer caches flattened
geometry separately from translation and coverage separately for fractional placement.
`MaximumPathCommands` bounds source outline commands, prepared geometry points and
cached coverage pixels independently. Coverage uses four bytes per pixel; at the
default two-million-pixel bound its retained array payload is about 7.6 MiB per renderer,
in addition to font and geometry storage. Exhausting a prepared-geometry or coverage
cache clears that cache; glyphs larger than the coverage budget render directly from
geometry. These caches belong to the renderer, rather than a global font cache.

Create a separate renderer and pixel buffer for each concurrent preview. Dispose the
pixel buffer after use, or transfer its storage to a frame with `PublishVideoFrame`.
The [drawing comparison](../benchmarks/Mediana.DrawingComparison/results/2026-11-06/README.md)
records time, allocation, peak-memory and quality results for these APIs.

Word and SVG support color artwork through this shared provider. Word owns paragraph
layout and pagination; SVG owns text anchors, positioning, paths and scene effects.
PDF retains its encoded character maps, embedded fonts, widths and text modes: its
external font names are mapped to shared family/style requests, while glyph meaning
stays with the PDF. PDF does not replace encoded glyphs with Unicode emoji artwork.
TGS text layers, general complex-script shaping and OpenType color-font tables remain
open capabilities.

SVG positioning indexes Unicode code points independently of grapheme selection,
as specified in [SVG 2 text layout](https://www.w3.org/TR/SVG2/text.html).
