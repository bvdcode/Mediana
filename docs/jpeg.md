# Sequential JPEG images

`Mediana.Codecs.Jpeg` supplies `JpegImageDecoder`, an `IImageDecoder` and
`IImageInfoReader` implementation. Register it with
`MediaEngineBuilder.AddImageDecoder`. The CLI recognizes the JPEG signature for
explicit or implicit `probe` and for `-i input output.png`, irrespective of the
input filename extension.

The decoder implements 8-bit Huffman sequential DCT: baseline SOF0 and extended
sequential SOF1. Grayscale and three-component images support interleaved and
separate component scans, sampling factors from one to four, optimized Huffman
tables, byte stuffing and restart intervals with modulo-eight marker sequencing.
SOF1 also supports table selectors two/three and 16-bit quantization entries.
Quantization tables are applied at the component's scan; redefinition between
sequential component scans does not change previously reconstructed samples.

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
`MediaBufferAllocator`; full-image coefficient arrays and duplicate RGBA arrays
are not allocated. Padded component planes remain live through reconstruction
and are released before the call returns. Their byte count is the sum of each
component's padded MCU width times padded MCU height; output requires width ×
height × 4 bytes. Small bounded tables/metadata and stack transform workspace are
outside the media byte-array budget. This budget is not whole-process memory.

`JpegDecodeOptions` defaults to 64 MiB encoded data, 16,777,216 output pixels and
100,000 markers. Cancellation is checked while parsing and at MCU/output-row
boundaries. Failure releases all component/output leases; the returned frame
remains valid after the encoded input and container are released.

## Verification and current scope

The 23-image independent corpus checks complete grayscale/RGB pixels, odd edges,
4:4:4/4:2:2/4:2:0, multiple component scans, quantization redefinition, SOF1 wide
tables and restart wrap. Expected pixels come from independent libjpeg decoding;
the maximum permitted difference is four 8-bit color codes for IDCT/interpolation
rounding, with exact opaque alpha. Actual extracted JPEG covers from FLAC/M4A
have a two-code guard. Exact working budgets, truncation at every byte of a small
image, invalid tables/runs/padding/restarts, metadata conflicts and cancellation
are shared between managed and Native AOT checks.

Actual managed and Linux Native AOT CLI outputs were also independently read
back for 21 JFIF corpus images and one complete 2560×2560 image. All 22 PNG pairs
are byte-identical; every decoded color component differs from the independent
reference by at most three 8-bit codes, with exact opaque alpha. Six native probes
include unlabelled and Adobe RGB headers. Four failures preserve an approved
existing destination: truncated input, unresolved component color, progressive
coding and an embedded ICC profile requiring an unavailable transform.

Progressive SOF2, arithmetic/lossless/hierarchical JPEG, 12-bit samples, DNL,
CMYK/YCCK, Exif presentation/color metadata, SPIFF and ICC conversion are not
implemented. These encodings are rejected explicitly. PDF `DCTDecode` integration
is separate from standalone image registration.

Format references: [ITU T.81](https://www.w3.org/Graphics/JPEG/itu-t81.pdf),
Annexes A/B/C/E/F, and [JFIF 1.02](https://www.w3.org/Graphics/JPEG/jfif3.pdf).
