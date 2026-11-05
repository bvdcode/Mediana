# Word document rendering

`Mediana.Documents.Word` reads DOCX, paginates text and tables, and draws pages
directly into managed RGBA surfaces using `Mediana.Drawing`. The same renderer is
available to library consumers and the CLI. Rendering does not invoke Office,
LibreOffice, a native graphics library, or an intermediate PDF or SVG conversion.

## Library API

```csharp
using Mediana.Codecs.Png;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Documents.Word;
using Mediana.Drawing;
using Mediana.Processing;

byte[] input = await File.ReadAllBytesAsync("document.docx");
WordDocument document = WordReader.Read(input, new()
{
    AllowFractionalMeasurements = true,
    DefaultPageSize = WordPageSize.A4
});
WordLayout layout = WordRenderer.Layout(document, new()
{
    Text = new() { SubstituteMissingFonts = true }
});

MediaBufferAllocator allocator = new(256L * 1024 * 1024);
using PixelBuffer pixels = await WordRenderer.RenderAsync(layout, pageNumber: 1,
    options: new()
    {
        MaximumWidth = 1200,
        MaximumHeight = 1200,
        ImageColorResolver = SdrPreviewColorPolicy.Resolve,
        WarningHandler = Console.Error.WriteLine
    }, allocator: allocator);
using VideoFrame frame = pixels.PublishVideoFrame();
await using FileStream output = File.Create("page.png");
await PngEncoder.EncodeAsync(frame, output, allocator);
```

`WordDocument` is independent of the encoded input memory. `WordLayout` resolves
font outlines once and exposes the actual `PageCount`. Reuse it for multiple
pages and concurrent independent renders. Physical layout uses points; changing
output DPI or maximum dimensions does not change pagination. Page numbers in
`WordRenderer` are one-based. The returned `PixelBuffer` belongs to the caller;
publication transfers its storage to a `VideoFrame` without copying RGBA bytes.

Documents expose their Sections. Each section carries its own page size,
margins, start type and header/footer references. Layout.GetPageSize(pageNumber)
returns the selected page's actual size; PageSize describes the first page.
Next-page, odd-page and even-page starts insert the necessary breaks; inherited
headers and footers retain their variants. Continuous sections share a page when
their geometry matches. A continuous change of page geometry and next-column
starts remain explicit unsupported cases.

The example explicitly selects fractional measurements, an A4 page size when
the document omits it, font substitution, and SDR interpretation of missing
image color labels. Reader and layout warnings are available on the document
and layout, and delivered to the render warning handler. Declared image color
characteristics take precedence over the preview policy.

Embedded pictures use `Mediana.Imaging` for decoder selection and sRGB preparation.
Direct rendering defaults to managed PNG/JPEG; set `WordRenderOptions.Images` to
use a shared `ImageDecodeContext` with additional formats. Registered Word
decoding inherits the engine's image registry through `ImageDecodeRequest`.
Adding a decoder to that registry also makes it available to embedded pictures.
PNG honors the shared preferred-resolution hints to retain bounded scanline
decoding for large source pictures. Unsupported hints may leave other decoders
at full resolution; page placement still determines the visible picture size.

## Fonts

The library does not search the filesystem or download fonts. For exact fonts,
set `WordLayoutOptions.Text.FontResolver`; the callback receives family, bold and
italic attributes and returns a font program, or empty memory when unavailable.
The layout owns the decoded outlines after resolution. The existing bundled
CC0 Canada1500 and Aileron faces remain available without extra font files.
Unavailable fonts cause an error unless substitution is explicitly enabled.

`Mediana.Drawing.FontCollection.LoadAsync(fontFiles)` loads a bounded collection of
caller-supplied static TrueType files. Connect its `Resolve` and
`ResolveCharacter` methods to `FontResolver` and `FallbackFontResolver`:

```csharp
FontCollection fonts = await FontCollection.LoadAsync(fontFiles);
WordLayout layout = WordRenderer.Layout(document, new()
{
    Text = new()
    {
        FontResolver = fonts.Resolve,
        FallbackFontResolver = fonts.ResolveCharacter,
        SubstituteMissingFonts = true
    }
});
```

Font collection matching uses OpenType family and style metadata rather than
filenames. The CLI searches installed font directories and selects matching
faces; it can use installed Liberation or Carlito replacements for common
Microsoft families. Font changes are reported and can change line wrapping and
page count. Missing characters may use a supplemental font; if none contains
the character, substitution mode draws the font's missing-glyph symbol with a
warning. Optional [Fluent color emoji](emoji.md) are available through
`WordLayoutOptions.Text.InlineGraphicProvider` and enabled in the CLI.
Complex-script shaping and OpenType color-font tables are not implemented.

## Media engine and CLI

Register one package decoder for all Open XML document handlers:

```csharp
using Mediana.Documents.OpenXml;

MediaEngine engine = new MediaEngineBuilder()
    .AddImageDecoder(new OpenXmlImageDecoder([
        new WordPageDecoder(readerOptions, layoutOptions, renderOptions)
    ]))
    .Build();
```

`OpenXmlImageDecoder` recognizes ZIP bytes and selects a handler using the OPC
main-part relationship and content type. It does not depend on the filename or
a fixed `word/document.xml` path. Additional document families and 3MF models can register
their own `IOpenXmlPageDecoder` without competing for the same ZIP signature.
Word and 3MF use one package registration in the CLI.
Image decode requests use zero-based `PageIndex`, following the common image
codec contract. Metadata probing reports the declared first-page size in points;
it does not invent a page count from cached application properties or paginate
the document just to probe dimensions.

```sh
mediana -i document.docx page.png
mediana -i document.docx page-2.png --page 2
mediana probe document.docx
```

CLI pages render at up to 96 DPI, fit within 1200 × 1200 pixels and use two-times
supersampling. Output extension selects PNG; document recognition uses content.

## Coverage and validation

Rendering covers multiple sections with independent page geometry; paragraph and character style inheritance;
physical measurements; Latin, Cyrillic and available Unicode outlines; font
size/style/color; underline and strike; left/center/right/justified text;
explicit line/page breaks; tabs; numbering; paragraph spacing and indentation;
keep and widow controls; table grids, cell spans, borders and shading; inline
PNG/JPEG pictures; and default/first/even headers and footers. Stored field
results are displayed; fields are not recalculated.

All pages of seventeen DOCX documents were rendered through the public library
API. Their first pages were also rendered through the managed and Native AOT CLI
and visually compared with LibreOffice output. Tests check page selection,
physical pixel coordinates, custom font ownership, reusable layouts, concurrent
page rendering, PNG round trips, cancellation, warning failures and resource
release. The complete Native AOT test executable and CLI corpus passed. This does not establish pixel equivalence with Word: pagination still
differs on some documents, particularly with substituted fonts.

Continuous changes of page geometry, floating drawings, vertical table merges, multicolumn flow,
charts and bidirectional/complex-script layout remain open. Legacy binary DOC,
Excel and PowerPoint rendering are separate operations in the
[format catalog](formats/README.md).

Package parsing bounds compressed input, part count, expanded bytes, XML depth
and elements. Layout separately bounds pages, glyphs, font faces and path
commands. The raster allocator charges output, supersampling surfaces, decoded
pictures and PNG scanline storage to one live-buffer budget. Large PNG pictures
are sampled during decoding instead of allocating their full source RGBA raster;
every source scanline is still reconstructed and validated. JPEG pictures retain
their codec's source-resolution decoding. Font programs, encoded picture bytes,
layout objects and BCL decompression workspace have separate bounds rather than
being counted as raster buffers.

The measurement and style model follows [ECMA-376](https://ecma-international.org/publications-and-standards/standards/ecma-376/).
Font selection reads the OpenType [name](https://learn.microsoft.com/en-us/typography/opentype/spec/name)
and [OS/2](https://learn.microsoft.com/en-us/typography/opentype/spec/os2) tables.
Reduced PNG decoding retains the filter and Adam7 rules of the
[PNG specification](https://www.w3.org/TR/png-3/).
