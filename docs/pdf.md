# PDF page previews

`Mediana.Documents.Pdf` reads PDF structure and interprets page painting commands
with managed C# code. It uses Mediana's drawing, TrueType, JPEG, JPEG 2000 and JBIG2
implementations. There are no PDF parser packages, native libraries or external
processes in the runtime path. The module is packaged as `Mediana.Documents.Pdf`.

[TrueType glyph reading](fonts.md) describes the outline bounds and ownership
used for embedded TrueType fonts.

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
await using FileStream output = new("preview.png", FileMode.CreateNew, FileAccess.Write, FileShare.None);
await PngEncoder.EncodeAsync(frame, output, allocator, cancellationToken);
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

Page interpretation borrows a single decoded content stream directly, including
an array containing one stream. Multiple content streams are combined in their
original order into one exact-length array. Operands and graphics state span
stream boundaries, as required by [ISO 32000-1, Table 30](https://opensource.adobe.com/dc-acrobat-sdk-docs/pdfstandards/PDF32000_2008.pdf).
The page-content limit counts actual decoded bytes, including repeated stream
references; no separator bytes are added. Detached pages retain interpreted
commands and pixels independently of these temporary syntax buffers.

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
- Eight-bit sequential and progressive Huffman JPEG image XObjects with
  DeviceGray or DeviceRGB, chained byte filters, Decode arrays and color-key,
  explicit image and soft masks.

Known gaps include inline images, LZW/CCITT filters, encrypted documents,
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

## JPEG image interpretation

PDF `DCTDecode` uses the shared [JPEG component decoder](jpeg.md), with the
container's color and presentation rules. Image dictionary dimensions, component
count and eight-bit depth must agree with the encoded image. DeviceGray ignores
`ColorTransform`. For DeviceRGB, Adobe APP14 takes precedence over the PDF
`ColorTransform` parameter; otherwise zero selects RGB and one selects YCbCr,
with one as the default. JPEG application presentation metadata does not change
PDF image placement or color interpretation. These rules follow clause 7.4.8
and Table 13 of [ISO 32000-1](https://opensource.adobe.com/dc-acrobat-sdk-docs/pdfstandards/PDF32000_2008.pdf).

Color-key ranges apply to reconstructed eight-bit color samples before `Decode`.
An `SMask` overrides `Mask`, including a color-key array. Indirect color-key
arrays and explicit mask image references are distinguished. CMYK/YCCK JPEG,
ICC color conversion and JPEG images using other PDF color spaces remain open.

JPEG component samples write into the image's one final RGBA allocation.
There is no intermediate RGBA image. The remaining `MaximumImageBytes` budget
also bounds the live component samples and progressive coefficient workspace;
cached page images count toward that budget. The exact sequential grayscale
1×1 case needs 68 bytes: 64 padded component bytes and four RGBA bytes. Parser
objects, compressed streams and later page rendering have separate limits.
Extracting an independent `VideoFrame` from a retained `PdfImage` still copies
its pixels; direct DCT interpretation does not remove that ownership boundary.

The 57 authored PDF references compare complete extracted image pixels with
independent libjpeg output and complete page pixels with Poppler/Cairo at
72 DPI. They cover grayscale/RGB, sequential/progressive scans, APP14 priority,
explicit transforms, chained filters, Decode and masks. Each RGB component may
differ by at most four eight-bit codes; alpha matches exactly. Shared managed
and Native AOT checks also cover every-byte truncation of a small JPEG, invalid
parameters, exact workspace limits, cancellation, repeated image reuse and
detached page lifetimes. A 512×512 allocation check rejects a second full RGBA
buffer during image interpretation.

Actual managed and Linux Native AOT commands extract an image and render a page
from every reference: 228 complete PNG conversions. The PDF inputs use unrelated
filename extensions. Managed/native PNG pairs are byte-identical; independent
pixel readback has a maximum color difference of three codes and exact alpha.
General `-i input output.png` matches explicit default page rendering. A failed
extraction with overwrite enabled preserves the existing destination. The full
2,885-test suite, all 30 package-only Native AOT dependencies and the native CLI
pass alongside formatting and source license-header checks.

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

## Page-content allocation

A complete 1 MiB page-content sequence, consisting of whitespace followed by a
blue rectangle, was interpreted after the reader owned its input. Three
alternating pairs of Native AOT consumers measured these allocation medians:

| Content streams | Before, bytes | After, bytes |
| --- | ---: | ---: |
| 1 | 4,200,256 | 5,832 |
| 2 | 4,726,341 | 1,055,984 |
| 17 | 4,967,344 | 1,081,448 |

Public allocation tests reject the earlier implementation. Functional checks
cover decoded-byte limits, shared operands and graphics state, indirect arrays,
Flate streams, cancellation, invalid references and detached page lifetimes.

Two complete synthetic PDFs with 8 MiB of page content, divided into one or 17
streams, were rendered to 200×100 PNGs. Three alternating pairs of fresh Linux
x64 Native AOT CLI processes were pinned to CPU core 2:

| Content streams | CPU before, seconds | CPU after, seconds | Peak RSS before, KiB | Peak RSS after, KiB |
| --- | ---: | ---: | ---: | ---: |
| 1 | 0.057122 | 0.048307 | 62,012 | 37,800 |
| 17 | 0.063280 | 0.051192 | 66,688 | 45,792 |

These medians include process startup on a shared host. All 12 resulting PNGs
were byte-identical and every RGBA component matched the expected painting;
independent Poppler rendering also matched both complete inputs. Three existing
PDF previews produced six more outputs with identical bytes within each
before/after pair. These inputs exercise page-content storage rather than image
or font decoding; the measurements do not establish sustained rendering speed
or a whole-process memory bound.

## Exact byte-filter output

ASCIIHex, ASCII85 and RunLength decoding validate and measure the complete
encoded stream before allocating one exact decoded array. The second pass writes
directly into that array. Malformed endings, impossible tuples and exceeded
decoded limits fail before the decoded payload allocation. Cancellation is
checked in both passes. The decoded-stream cache retains the resulting storage.

ASCIIHex validates bounded runs with `SearchValues<byte>` and converts complete
pairs directly into the final span with `Convert.FromHexString`. Whitespace and
an unmatched nibble are handled across run boundaries; the final unmatched
nibble is padded with zero. ASCII85 zero tuples and RunLength repetitions fill
the destination directly, without allocating a temporary array per run.
These rules follow clauses 7.4.2, 7.4.3 and 7.4.5 of
[PDF 32000-1:2008](https://opensource.adobe.com/dc-acrobat-sdk-docs/pdfstandards/PDF32000_2008.pdf).

Public checks cover both long and abbreviated filter names, whitespace, partial
tuples, literal and repetition boundaries, exact limits, cancellation, malformed
streams and detached pixels. Allocation checks reject another full decoded
payload and require invalid or excessive data to fail before that allocation.

Three alternating pairs of Native AOT consumers measured page interpretation
after input opening for a 1024×1024 grayscale image:

| Filter | Before, bytes | After, bytes |
| --- | ---: | ---: |
| ASCIIHex | 7,356,120 | 5,253,744 |
| ASCII85 zero tuples | 15,740,050 | 5,256,664 |
| RunLength repetitions | 7,354,098 | 5,259,533 |

Five complete synthetic 2560×2560 RGB image extractions to PNG used three
alternating pairs of fresh Linux x64 Native AOT CLI processes on CPU core 2.
Ordinary samples were encoded as hexadecimal, base-85 and literal runs; zero
samples exercised base-85 zero tuples and repeated runs.

| Input | CPU before, seconds | CPU after, seconds | Peak RSS before, KiB | Peak RSS after, KiB |
| --- | ---: | ---: | ---: | ---: |
| ASCIIHex | 1.267381 | 1.038535 | 229,476 | 178,180 |
| ASCII85 | 1.116364 | 1.110444 | 182,496 | 157,828 |
| ASCII85 zero tuples | 0.506075 | 0.472305 | 124,608 | 102,144 |
| RunLength literals | 0.984262 | 0.988026 | 168,504 | 142,840 |
| RunLength repetitions | 0.528297 | 0.433921 | 141,696 | 89,732 |

All 30 complete PNGs were identical within their input case. Independent image
extraction with Poppler 24.02.0 and independent PNG decoding matched every
26,214,400-component RGBA image against its original samples. The three existing
PDF page previews also remained byte-identical. All 2,687 managed tests, the
complete strict Native AOT runner and CLI build, formatting and license-header
checks pass.

These medians include process startup and input reading on a shared host. They
describe complete image extraction for these inputs; they do not establish a
process memory ceiling or general page-rendering throughput.
