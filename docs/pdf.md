# PDF page previews

`Mediana.Documents.Pdf` reads PDF structure and interprets page painting commands
with managed C# code. It uses Mediana's drawing, TrueType, JPEG 2000 and JBIG2
implementations. There are no PDF parser packages, native libraries or external
processes in the runtime path. The module is packaged as `Mediana.Documents.Pdf`.

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
MediaBufferAllocator allocator = new(32 * 1024 * 1024);
using PixelBuffer pixels = PdfRenderer.Render(page,
    new PdfRenderOptions { MaximumWidth = 1200 }, cancellationToken, allocator);
using VideoFrame frame = pixels.PublishVideoFrame(cancellationToken);
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

The stream cache retains decoded filter output directly. Unfiltered streams are
read-only views of the document input; cached samples are not modified by image
interpretation. Detached pages own their RGBA pixels independently. Rendering
into an allocator-backed `PixelBuffer` and publishing it transfers its final
pixel storage into a frame without a second RGBA allocation.

Flate decoding borrows its encoded stream bytes for the duration of decoding.
Decompression and prediction produce independent decoded storage; the existing
decoded byte limits still apply. The encoded input is not copied into a second
complete array for the decompressor.

Encoded input reading and Flate output accumulation share a bounded stream
reader. It accumulates fixed 64 KiB segments and assembles an exact-length owned
array with at most one final copy. The segment storage is bounded by the byte
limit; the final array and object overhead are additional memory. Caller streams
remain open, including cancellation and read failures. Exact limits require an
extra one-byte read to distinguish EOF from oversized data.

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

## Stream cache allocation measurement

For an unfiltered 1000×500 RGB image, page interpretation after the reader owns
its input allocates a median 2,015,282 bytes instead of 3,519,240 bytes. This
includes the independent RGBA pixels and interpretation objects. A regression
check rejects another complete 1,500,000-byte RGB stream copy. Repeated filtered
and unfiltered image reads also check cumulative decoded limits, stencil colors,
caller stream ownership and detached pixel lifetimes.

A separate complete `pdf image` conversion of a synthetic 2560×2560 RGB XObject
to PNG used three alternating pairs of fresh Linux x64 Native AOT processes on
one CPU core. Every output PNG was byte-identical. Independent decoding matched
all 26,214,400 RGBA components, and the `graphics`, `report` and `scan-globals`
page previews also remained byte-identical.

| Whole conversion metric | Before | After |
| --- | ---: | ---: |
| Median CPU seconds | 1.058529 | 1.090124 |
| Median peak RSS, KiB | 181,888 | 162,604 |
| Peak RSS range, KiB | 138,128–181,888 | 141,568–162,944 |

The CPU median increased 2.98%; this change establishes no speedup. RSS ranges
overlap on the shared host. The allocation result establishes removal of the
cache copy; these process measurements do not establish a general memory bound.

## Bounded input and Flate accumulation

For the same 1000×500 RGB samples encoded as valid compressed and stored zlib
streams, page interpretation after input opening has these allocation medians:

| Stream encoding | Before, bytes | After, bytes |
| --- | ---: | ---: |
| Compressed zlib | 7,718,306 | 5,028,920 |
| Stored zlib | 9,210,517 | 5,025,568 |

A complete 2560×2560 stored-zlib PDF image extraction to PNG used three
alternating pairs of fresh Native AOT processes on CPU core 2. Every complete
PNG was byte-identical, independent decoding matched all 26,214,400 RGBA
components, and the three existing PDF page previews remained byte-identical.

| Whole conversion metric | Before | After |
| --- | ---: | ---: |
| Median CPU seconds | 1.040697 | 1.013606 |
| Median peak RSS, KiB | 215,808 | 159,144 |
| Peak RSS range, KiB | 215,680–215,936 | 158,704–163,172 |

The observed CPU reduction is 2.60% on the shared host. A separate Native AOT
consumer performing the same library operations recorded cumulative allocation
medians of 86,845,504 → 39,481,296 bytes after input reading and
275,614,536 → 161,241,896 bytes after PNG encoding. These are scoped workload
measurements; collection timing and other inputs can change process peaks.
