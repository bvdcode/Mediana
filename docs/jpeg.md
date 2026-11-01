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

## Pixels, interpretation and ownership

Output is independent opaque RGBA32. The inverse DCT uses the mathematical
separable transform, with a DC-only fast path. Subsampled components use centered
bilinear reconstruction with edge extension according to JFIF sample positions.
YCbCr uses the full-range JFIF matrix. No gamma conversion is performed; source
primaries and transfer remain unspecified. The CLI uses its explicit RGB preview
interpretation for missing labels and warns once.

JFIF identifies grayscale or YCbCr; Adobe APP14 identifies RGB or YCbCr for
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

Numbered ICC parts are checked for consistency and completeness. The decoder
retains ICC presence in `JpegColorDescription`, without assembling or retaining
the profile. Converting that description is explicitly unsupported; the CLI
does not replace a declared ICC profile with its missing-label policy.

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
an embedded ICC profile requiring an unavailable transform. The complete managed
suite passes 2,852 tests; all 30 library packages also pass the isolated Linux
Native AOT consumer and native shared checks.

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
CMYK/YCCK, Exif presentation/color metadata, SPIFF and ICC conversion are not
implemented. These encodings are rejected explicitly. PDF `DCTDecode` integration
is separate from standalone image registration.

Format references: [ITU T.81](https://www.w3.org/Graphics/JPEG/itu-t81.pdf),
Annexes A/B/C/E/F/G, and [JFIF 1.02](https://www.w3.org/Graphics/JPEG/jfif3.pdf).
