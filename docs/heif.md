# HEIC still images

`Mediana.Images.Heif` is a source module containing a managed HEIF item reader
and intra HEVC decoder. It depends only on Mediana.Core and the BCL. The code was
adapted from the same author's MIT-licensed Heic.NET implementation at commit
`3eefb9f`; Mediana owns its asynchronous decoding and cancellation path.

```csharp
using Mediana.Images.Heif;

await using FileStream input = File.OpenRead("image.heic");
HeicDocument document = await HeicDocument.LoadAsync(input,
    new HeicDecodeOptions(maxPixels: 16_777_216), cancellationToken);
RgbaImage image = await document.DecodePrimaryAsync(cancellationToken);
ReadOnlyMemory<byte> rgba = image.Pixels;
```

`DecodeImageAsync` selects an item by its identifier. Loading owns a copy of the
input and leaves the source stream open. Each decode owns independent RGBA8
pixels. Grids and wavefront rows use bounded parallel workers; every worker
finishes before its picture storage is released. Cancellation is checked at
image boundaries and during coding-tree reconstruction. Filters and color
conversion may finish their current synchronous stage before cancellation returns.

Supported containers include `heic`/`heix`, primary images, `hvc1`, `grid`, `iden`
and `iovl` items, auxiliary monochrome alpha, integral clean aperture, rotations
and mirrors. HEVC supports single-layer 8/10-bit intra IDR/CRA/BLA pictures,
monochrome and 4:2:0/4:2:2/4:4:4, independent slices, wavefront coding, scaling
lists, deblocking and SAO. This module does not expose a general HEVC video
session or encode HEIC files.

Returned pixels use straight alpha. Ordered container transformations and alpha
composition are already applied. RGB retains the signalled CICP primaries and
transfer function; embedded ICC bytes are retained in `ColorInformation`.
RGB is not implicitly sRGB. `RgbaImage.ToVideoFrame` copies pixels into a Mediana buffer while preserving CICP color interpretation and requiring an explicit pixel aspect ratio; it rejects ICC-profiled images. ICC transforms, HDR tone mapping, fractional clean
aperture, pixel-aspect correction, HEVC inter prediction, tiles, dependent
slices, 12-bit/multilayer coding and protected/external items are unsupported.

Input bytes, pixels per image, item count and derivation depth are bounded by
`HeicDecodeOptions`. These are admission limits, not a total working-memory or
RSS limit. Large grids and intermediate images require additional memory.

Tests verify complete RGBA hashes and visible dimensions for 42 independent
synthetic HEIC references. Three additional 8/10-bit/monochrome wavefront
streams match independent plane hashes with one and four workers. Further checks
cover input ownership, concurrent results, item selection, malformed input,
limits and cancellation. This coverage does not establish full format conformance.
