# PDF page previews

`Mediana.Documents.Pdf` reads PDF structure and interprets page painting commands
with managed C# code. It uses Mediana's drawing, TrueType, JPEG 2000 and JBIG2
implementations. There are no PDF parser packages, native libraries or external
processes in the runtime path. The module is experimental and not yet packaged.

```csharp
using Mediana.Documents.Pdf;
using Mediana.Drawing;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Codecs.Png;

using PdfDocument document = await PdfReader.ReadFileAsync("report.pdf",
    cancellationToken: cancellationToken);
PdfPageInfo info = document.GetPageInfo(1);
PdfPage page = document.GetPage(1, cancellationToken);
PixelBuffer pixels = PdfRenderer.Render(page,
    new PdfRenderOptions { MaximumWidth = 1200 }, cancellationToken);
MediaBufferAllocator allocator = new(32 * 1024 * 1024);
using VideoFrame frame = pixels.ToVideoFrame(allocator, cancellationToken);
using MediaBuffer png = PngEncoder.Encode(frame, allocator, cancellationToken);
```

Page numbers start at one. Width and height are visible dimensions in points
(1/72 inch), after inherited MediaBox/CropBox, clockwise rotation and UserUnit.
DPI defaults to 96; maximum dimensions reduce the page proportionally. Pixel
extents round upward without stretching the geometry. Supersampling defaults to
two. Output is straight sRGB RGBA8 on an opaque white background by default.

A page owns its interpreted paths and image pixels and remains usable after the
document is disposed. It can be rendered repeatedly or concurrently. Access to a
document's parser is serialized. Input streams remain open; files and bounded
non-seekable streams are accepted. Decoding a document retains encoded data and
caches resolved objects/streams, rather than streaming through an entire file
with constant memory.

`page.Images` exposes decoded image XObjects reached during interpretation,
including mask images. Each has a resource name, dimensions, RGBA bytes and
`ToVideoFrame`. These are decoded pixels with PDF sample conversion/masks, not
copies of the original compressed image files. Stencil images are interpreted
using the current fill ink. Unreferenced image resources are not enumerated.

The implemented subset includes:

- Classic cross-reference tables, cross-reference streams, compressed object
  streams and incremental `/Prev` chains, indirect stream lengths and inherited
  page resources.
- Flate, ASCII85, ASCIIHex and RunLength filter chains, PNG predictors and 8-bit
  TIFF prediction. Stream ends and declared lengths are checked.
- Ordered paths, nonzero/even-odd fills, affine strokes, dashes, clipping,
  save/restore, nested Form XObjects and their bounding boxes.
- Fill/stroke opacity and named PDF blend modes on the RGB canvas.
- Embedded TrueType simple fonts (WinAnsi and supported Differences, or the
  font's Macintosh `(1,0)` cmap for symbolic codes) and Identity-H CIDFontType2.
  PDF widths, text matrices, spacing, rise, fill/stroke/invisible/clip modes are
  applied. Embedded outlines are used; no font substitution is applied.
- Raw or filtered image XObjects with 1/2/4/8/16-bit samples, DeviceGray/RGB/CMYK,
  Indexed palettes, Decode arrays, color-key masks and image/soft masks. CMYK
  conversion is an arithmetic RGB preview, without ICC-managed printing.
- JP2 images declaring their own supported color space, and JBIG2 including
  chained filters and indirect compressed JBIG2Globals.

Known gaps include JPEG, inline images, LZW/CCITT filters, encrypted documents,
Type 1/CFF/Type 3 or unembedded font outlines, general CMaps and glyph-name
coverage, gradients/patterns, ICC color transforms, transparency groups,
graphics-state soft masks, annotations and optional content. JPX color-space
and opacity overrides and soft-mask Matte correction are also open. Text
extraction, forms editing, signatures and OCR are outside the preview API.

Known unsupported painting is reported in `UnsupportedFeatures`; rendering
rejects it by default. Unsupported structure/codecs may fail while opening or
interpreting a page. Explicit `AllowPartialRendering` can permit reported
painting gaps; it does not bypass malformed input or resource limits. A clean
feature list is not proof of complete PDF conformance.

Encoded bytes, cumulative cached decoded streams, object numbers, parsed values,
page count, nesting, page operations/glyph geometry, decoded image bytes and
output samples have separate limits. They are not a whole-process memory cap.
Cancellation is observed while reading, parsing, decoding, interpreting and
rendering. Fonts and some codec internals can finish bounded synchronous work
before cancellation is observed.

The parser and font mapping follow [PDF 1.7, ISO 32000-1:2008](https://opensource.adobe.com/dc-acrobat-sdk-docs/pdfstandards/PDF32000_2008.pdf),
particularly sections 7.3–7.5, 7.7.3, 8.3, 8.5, 8.9 and 9.6.6. Drawing command
adapters derive from the same author's MIT-licensed Render.NET commit `03cb502`;
PDF parsing and interpretation are implemented within Mediana.

Tests use authored PDF files and normative structure vectors. Full images of a
vector page and an embedded-font report are compared against Poppler 26.05.0,
including dimensions and every RGB/alpha sample. The comparison requires mean
RGB error at most 3/255 and at least 95% of pixels within 8 per channel; alpha
matches exactly. Antialiasing and unhinted glyph edges differ. The report's
observed mean error is 2.19/255 with 95.67% within 8. Extracted JBIG2 pixels match
the original synthetic scan exactly. These are bounded regression comparisons,
not arbitrary-document conformance evidence. Poppler is used for development
verification only.
