# JPEG images

`Mediana.Codecs.Jpeg` supplies `JpegImageDecoder`, an `IImageDecoder` and
`IImageInfoReader` implementation. Register it with
`MediaEngineBuilder.AddImageDecoder`. The CLI recognizes the JPEG signature for
explicit or implicit `probe` and for `-i input output.png`, irrespective of the
input filename extension.

The decoder implements 8-bit Huffman DCT: baseline SOF0, extended sequential
SOF1 and progressive SOF2. Grayscale and three-component images support
interleaved and separate component scans, sampling factors from one to four, optimized Huffman
tables, byte stuffing and restart intervals with modulo-eight marker sequencing.
SOF1/SOF2 also support table selectors two/three and 16-bit quantization entries.
Quantization tables are applied at the component's scan; redefinition between
sequential component scans does not change previously reconstructed samples.

Progressive scans support spectral selection, successive approximation, signed DC
refinement, new AC values and corrections to existing coefficients. AC bands can
arrive in any order after that component's first DC scan; AC scans contain one
component, while DC scans may interleave components. Each refinement improves
precision by one bit and must match the preceding coefficient-band precision.
End-of-band runs remain within the scan and restart interval. Quantization cannot
change after a progressive component's first scan. A DC-only or partially refined
progression reconstructs its transmitted coefficients; unseen AC values remain
zero.

Marker lengths, component uniqueness, table references, canonical Huffman code
space, coefficient categories/runs, restart boundaries, one-bit padding, complete
component coverage and final EOI are validated. Truncated entropy data is rejected
without inventing missing coefficients or pixels. Bytes after EOI are rejected.
Unknown application segments and comments are skipped; recognized unsupported
presentation formats are reported explicitly.

## Pixels and source interpretation

Output is independent opaque RGBA32. The inverse DCT uses the mathematical
separable transform, with a DC-only fast path. Subsampled components use bilinear
reconstruction with edge extension at the declared JFIF/Exif sample positions.
YCbCr uses the full-range JFIF matrix. No gamma conversion is performed.
Source primaries and transfer remain unspecified unless Exif identifies sRGB.
The CLI uses its explicit RGB preview interpretation for missing labels and
warns once.

JFIF and Exif identify YCbCr; Adobe APP14 identifies RGB or YCbCr for
three components. Missing declarations require a known interpretation supplied
by the caller, rather than deriving color from numeric component identifiers.
For example, the original JPEG covers in the FLAC/M4A references are known
full-range YCbCr with square pixels:

```csharp
JpegImageDecoder jpeg = new(sourceColorSpace: JpegColorSpace.YCbCr,
    sampleAspectRatio: SampleAspectRatio.Square);
```

Supplied color/aspect information must agree with any corresponding declaration.
JFIF density determines sample aspect ratio as Ydensity/Xdensity. Without a density
declaration or explicit aspect, it remains unknown. Header probing can report
dimensions without resolving an absent color interpretation; this is not proof
that entropy data or color conversion will decode successfully.

Numbered ICC parts are checked for consistency and completeness, then assembled
in order into one owned `IccColorDescription`. The frame retains the original
profile after input memory is released. Common sRGB processing applies supported
ICC v2/v4 RGB and gray matrix/TRC profiles; the CLI never replaces the profile
with its missing-label policy. See [ICC conversion](icc-color.md).

## Exif presentation

TIFF requires directories and out-of-line values to begin on word boundaries
([TIFF 6.0, section 2](https://download.osgeo.org/libtiff/doc/TIFF6.pdf)). The JPEG
presentation reader rejects unaligned primary/color-directory and recognized
rational offsets by default. To read such exports at their declared byte positions:

```csharp
JpegImageDecoder jpeg = new(new JpegDecodeOptions
{
    AllowUnalignedExifOffsets = true
});
ImageDecodeRequest request = new() { WarningHandler = warnings.Add };
using VideoFrame image = await jpeg.DecodeAsync(encoded, request, allocator, cancellationToken);
```

The option preserves bounds, type, count, value and conflict validation. A decode
reports one warning through the request; inspection retains warnings in the
`media.warnings` metadata property. The CLI enables this option for both probing
and conversion. Truncated values and offsets into the TIFF header remain errors.

Standalone decoding reads the primary Exif TIFF directory and its Exif color
directory in either byte order. Offsets, directory extents, ordered unique tags,
recognized scalar types/counts and rational values are checked within the APP1
segment. GPS, maker notes, text and thumbnail pixels are not retained or decoded.
The thumbnail's orientation does not change the primary image.

All eight primary orientations are applied while writing the final RGBA pixels,
including both reflections and axis transpositions. Header probing reports the
same presented dimensions. The shared core `OrthogonalTransform` supplies the
coordinate mapping used by both codecs and display fitting. Component planes
are sampled directly into the oriented output; no unrotated RGBA intermediate
is created. A sequential 35×19 4:2:0 image rotated to 19×35 needs exactly
4,964 media bytes: 2,304 padded component bytes and 2,660 final RGBA bytes.

Exif declares square pixels. JFIF density or a caller's supplied aspect must
agree. Exif `YCbCrPositioning` selects centered or co-sited reconstruction; absent
positioning defaults to centered. Conflicting JFIF/Exif chroma positions fail by
default. Set `JpegDecodeOptions.ChromaConflictPolicy` to
`JpegChromaConflictPolicy.UseJfif` to select centered JFIF positioning and report a
warning through `ImageDecodeRequest.WarningHandler`. The CLI enables this policy
for its common image registry, including JPEG images embedded in documents.
Without a conflict, the declared Exif positioning remains in effect. Invalid
Exif values and conflicts involving color or pixel aspect remain errors.
Chroma reconstruction preserves the sampling interval at odd image edges.

```csharp
JpegImageDecoder jpeg = new(new JpegDecodeOptions
{
    ChromaConflictPolicy = JpegChromaConflictPolicy.UseJfif
});
```

Managed and Native AOT checks compare conflicting 4:2:2 and 4:2:0 files against
independent centered references, in both APP0/APP1 orders. They verify the strict
default, unchanged co-sited files, pixel-limit propagation, combined warnings,
and buffer release if the diagnostic handler stops decoding.

Exif `ColorSpace=1` labels the reconstructed pixels as sRGB. Explicitly
uncalibrated color produces `JpegExifColorDescription`, whose conversion fails
until calibrated interpretation is supported. Custom Exif transfer functions,
chromaticities, gamma, YCbCr coefficients and reference levels are unsupported.
An ICC profile takes precedence over Exif color labels and custom colorimetry.
Supported ICC transforms use the common sRGB converter. Uncalibrated declarations
and unsupported ICC models do not use the CLI missing-label policy.

These semantics follow CIPA DC-008-2026 (Exif 3.1), clauses 4.4.2–4.4.3,
4.6.2, 4.6.5.1.6, 4.6.5.1.8–9, 4.6.5.1.13 and 4.6.6.2, available from
[CIPA's standards catalogue](https://www.cipa.jp/e/std/std-sec.html).

The 38 authored references cover both byte orders, all eight orientations with
sequential/progressive JPEG, default orientation, a different thumbnail
orientation, JFIF coexistence, grayscale and co-sited 4:2:2/4:2:0. Complete
independent pixels use libjpeg/Pillow; co-sited references use independent native
component decoding and floating-point interpolation. Tests require color error
at most four codes and exact alpha. Truncated TIFF data, invalid pointers,
duplicate/conflicting declarations, unknown colors, cancellation, detached input
and exact one-byte allocation boundaries are checked separately.

Managed and Linux Native AOT CLI checks produce 76 complete PNGs from
renamed Exif inputs and run 76 probes. Every PNG pair is byte-identical, complete
independent RGB error is at most three codes and alpha is exact. An independently
muxed MP3 preserves the original Exif picture through extraction and common
selection; its managed/native decoded PNGs equal direct JPEG conversion.
Malformed Exif and uncalibrated-color failures preserve an existing destination.

Two batches of three alternating fresh native process pairs measured complete
2560×2560 ordinary JPEG → PNG conversion on one core with
`DOTNET_PROCESSOR_COUNT=1`:

| Batch | CPU before Exif support | CPU with Exif support | Peak RSS before, KiB | Peak RSS with Exif, KiB |
| --- | ---: | ---: | ---: | ---: |
| 1 | 1.656964 s | 1.737736 s | 82,992 | 83,456 |
| 2 | 1.636282 s | 1.616270 s | 83,456 | 83,328 |

Complete PNG bytes are identical. The CPU difference changes from +4.87% to
−1.22% across the shared-host batches; these measurements establish neither a
consistent slowdown nor a speedup. A separate phase-precomputation variant used
1.651678 s in the second batch versus 1.616270 s for the selected implementation
and is not used. Timings include startup, decoding, PNG encoding, logging and I/O.

## Memory and ownership

Encoded memory is borrowed for the call, including when supplied to the common
`MediaEngine.DecodeImageAsync(ReadOnlyMemory<byte>, ...)` overload. Component
sample planes and the one returned RGBA array are accounted by
`MediaBufferAllocator`. Sequential decoding reconstructs each block immediately
without a full-image coefficient array. Progressive decoding additionally charges
four bytes per padded component sample for retained quantized coefficients. Each
coefficient plane is released immediately after its component samples have been
reconstructed; all coefficient planes are released before allocating the final
RGBA array. Neither path duplicates the full RGBA output.

Padded component planes remain live through pixel conversion and are released
before the call returns. Their byte count is the sum of each component's padded
MCU width times padded MCU height; output requires width × height × 4 bytes.
The exact 1×1 grayscale working budgets are 68 bytes for sequential and 320 bytes
for progressive. Small bounded tables/metadata and stack transform workspace are
outside the media byte-array budget. This budget is not whole-process memory.

`JpegDecodeOptions` defaults to 64 MiB encoded data, 16,777,216 output pixels and
100,000 markers. Cancellation is checked while parsing, at MCU/output-row
boundaries and during progressive reconstruction. Failure releases all
component/output leases; the returned frame remains valid after the encoded input and container are released.

## Container component decoding

`JpegDecoder.DecodeComponents` returns a disposable `JpegImage` owning the native
component samples. It uses the same entropy decoding, IDCT and subsample
reconstruction as standalone image decoding. Encoded input is borrowed only for
the call and can be released after it returns. The component image exposes
dimensions, component count and the original nullable Adobe APP14 transform.

`JpegImage.WriteRgbaTo(destination, colorSpace, cancellationToken)` writes into
caller-owned storage with an explicit RGB or YCbCr interpretation. It allocates
no output array, writes exactly width × height × 4 bytes and preserves any tail.
Grayscale ignores the three-component interpretation. Cancellation may leave
the destination partially written; disposing the image releases the component
leases. Writing after disposal fails.

This entry point does not infer presentation from JFIF, Exif, ICC or other
application metadata. The enclosing format supplies those semantics. PDF uses
it for its own APP14/ColorTransform, Decode and mask rules, described in
[PDF image interpretation](pdf.md#jpeg-image-interpretation). Standalone
`JpegImageDecoder` retains its existing metadata validation and explicit color
requirements. Its native dimensions and centered reconstruction are independent
of standalone Exif orientation and chroma positioning.

The 47-image corpus also checks this path after clearing the encoded source.
Checks require complete output pixels, exact tail preservation, bounded sample
ownership, allocation-free repeated writes, cancellation and disposal behavior.

Three alternating pairs of fresh Linux Native AOT processes checked the shared
writer change with complete 2560×2560 sequential JPEG → PNG conversion on one CPU
core and `DOTNET_PROCESSOR_COUNT=1`:

| Metric | Before component API | With component API |
| --- | ---: | ---: |
| Median CPU seconds | 1.616669 | 1.633823 |
| Median wall seconds | 1.617009 | 1.634093 |
| Median peak RSS, KiB | 82,988 | 83,200 |

All six PNGs are byte-identical. The CPU difference is +1.06% on the shared host;
this functional extension establishes no speedup. These measurements include
startup, decoding, PNG encoding, logging and file I/O.

## Verification and current scope

The 47-image independent corpus checks complete grayscale/RGB pixels, odd edges,
4:4:4/4:2:2/4:2:0, multiple component scans, sequential quantization redefinition,
SOF1 wide tables and restart wrap. Its 24 progressive references include spectral
bands sent out of frequency order, combined refinement bands, new signed AC
values, signed corrections, merged end-of-band runs and a coarse DC-only image.
Expected pixels come from independent libjpeg decoding;
the maximum permitted difference is four 8-bit color codes for IDCT/interpolation
rounding, with exact opaque alpha. Actual extracted JPEG covers from FLAC/M4A
have a two-code guard. Exact working budgets, truncation at every byte of small
sequential and progressive images, invalid tables/runs/padding/restarts, metadata
conflicts and cancellation are shared between managed and Native AOT checks.
Progressive checks also reject repeated/overlapping bands, refinement without
its preceding precision, AC before DC, quantization changes and EOB runs crossing
a scan or restart boundary.

Actual managed and Linux Native AOT CLI outputs were also independently read
back for 45 JFIF corpus images and two complete 2560×2560 images, encoded as
sequential and progressive JPEG. All 47 PNG pairs are byte-identical; every
decoded color component differs from the independent reference by at most three
8-bit codes, with exact opaque alpha. Ten native probes include progressive,
unlabelled and Adobe RGB headers. Four failures preserve an approved existing
destination: truncated input, unresolved component color, arithmetic coding and
a malformed embedded ICC profile.

An additional complete native conversion checks progressive 16-bit quantization,
quantization/Huffman destination three and signed DC refinement. The AC table is
installed after the DC scans, before explicitly transmitting the zero AC band;
unused AC tables are not required by the DC scans.

Seven alternating fresh native processes per variant measured complete
2560×2560 JPEG → PNG conversion with `DOTNET_PROCESSOR_COUNT=1`:

| Input and decoder | Median CPU | Median wall time | Median peak RSS |
| --- | ---: | ---: | ---: |
| Sequential, before shared scan traversal | 1.536 s | 1.590 s | 80.6 MiB |
| Sequential, with shared scan traversal | 1.558 s | 1.594 s | 80.6 MiB |
| Progressive, with coefficient retention | 1.606 s | 1.658 s | 118.1 MiB |

The sequential CPU difference is +1.4%; its before/after PNG bytes are identical.
These local process measurements include startup, parsing, IDCT, pixel conversion,
PNG encoding, logging and I/O. Peak RSS includes runtime and collection behavior;
it is separate from the exact live media-buffer budget.

Arithmetic/lossless/hierarchical JPEG, 12-bit samples, DNL,
CMYK/YCCK, custom Exif colorimetry without ICC, SPIFF and ICC LUT conversion are not
implemented. These encodings are rejected explicitly by standalone decoding.
PDF `DCTDecode` integration uses the container component API described above.

Format references: [ITU T.81](https://www.w3.org/Graphics/JPEG/itu-t81.pdf),
Annexes A/B/C/E/F/G, and [JFIF 1.02](https://www.w3.org/Graphics/JPEG/jfif3.pdf).

## Reduced component resolution

`JpegDecoder.DecodeReducedComponents` accepts a reduction factor of 1, 2, 4 or 8.
It decodes the complete entropy stream and reconstructs 8×8 DCT blocks into
8×8, 4×4, 2×2 or 1×1 sample blocks. `JpegImage.Width` and `Height` describe
reduced pixels; `EncodedWidth` and `EncodedHeight` preserve source dimensions.
The factor-one path uses the existing full-resolution transform.

Source pixel limits still apply. Sequential sample allocation decreases with
reduction; progressive coefficient retention remains at source resolution and
counts toward the allocator budget. Exact minimal grayscale sample budgets are
64, 16, 4 and 1 byte for the four factors, before any RGBA destination.

Twenty-four reduced grayscale, RGB and Adobe RGB references compare every RGBA
component with independent libjpeg/Pillow reduced decoding, allowing three
color codes and requiring exact alpha. The full 47-image corpus also checks
reduced dimensions, ownership and allocator release, including subsampled odd
edges; those latter checks do not establish independent reduced chroma color
accuracy. Cancellation, truncation and progressive workspace limits remain
covered.

## Redundant final restart marker

After decoding every MCU and checking entropy padding, the decoder can recover
one redundant restart marker at the end of a complete restart interval. The
remaining input must be exactly the expected `RST0`–`RST7` marker followed by
`EOI`, with no other bytes. The marker still counts toward `MaximumMarkers`.
Wrong numbers, incomplete intervals, premature markers, repeated markers,
additional scans/segments and bytes after EOI remain errors. The decoder does
not search ahead or change reconstructed coefficients and samples.

This narrow recovery handles a malformed encoding: T.81 Annex B.2.1 requires
restart markers between entropy segments and excludes one after the last
segment. `JpegImage.Warnings` reports the recovery for component callers;
`JpegImageDecoder` reports it through `ImageDecodeRequest.WarningHandler`.
Header probing does not decode MCU data and cannot establish this recovery's
conditions.

Shared managed and Native AOT checks compare every pixel with the corresponding
JPEG without the redundant marker. They cover sequential and progressive DC
scans, restart sequence wrap, factors 1/2/4/8, exact sample/output budgets,
malformed endings and diagnostic-handler failure. CLI tests run complete
processes and check both the warning and output publication.
