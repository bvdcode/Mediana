# OpenEXR decoding

`Mediana.Codecs.OpenExr` supplies `OpenExrImageDecoder` through the common image
metadata and decoding interfaces. CLI and `Mediana.Imaging` register it with the
other raster decoders. `PageIndex` selects a zero-based multipart image; CLI
`--page` remains one-based. `OpenExrDecodingOptions.Layer` selects a named channel
layer, such as `beauty` for `beauty.R/G/B/A`.

## Supported representations

- Version 2 single-part and multipart scanline images.
- HALF and FLOAT color channels, including mixed precision; RGB/RGBA or Y/YA.
- Uncompressed, RLE, ZIPS (one row) and ZIP (up to 16 rows) chunks, including
  uncompressed chunks retained by the writer when compression is not smaller.
- Increasing/decreasing storage order with indexed chunk access; negative data
  window origins and distinct display windows.
- Explicit layer selection, alpha, pixel aspect, chromaticities and native
  metadata attributes. Extra unit-sampled channels are skipped while decoding
  the selected color layer; UINT auxiliary data can remain in the file.

Tiled/deep pixel data, PIZ/PXR24/B44/DWA/HTJ2K compression, subsampled channels,
UINT color output and channel-only scientific data without RGB or Y are not
pixel-decoded. Metadata inspection retains declared part/channel information;
part layouts without a supported or declared chunk count are rejected. Incomplete
files with zero chunk offsets are not reconstructed. Required attributes, chunk
counts, scanline coordinates, part numbers, compressed lengths and checksums are
validated. Damaged data is not replaced with synthesized samples.

## Samples and presentation

The decoded frame covers the **data window**, with its original origin and the
separate display window available in metadata. Its pixel format is
`VideoPixelFormat.RgbaFloat32Premultiplied`: native-endian IEEE float32 RGBA with
OpenEXR's premultiplied alpha convention. HALF converts exactly to float32.
Negative values, values above one and IEEE infinities/NaNs remain available in the
decoded frame. Missing alpha is one. Decoding does not apply exposure, gamma,
clipping or a tone curve.

OpenEXR's default color interpretation is scene-linear BT.709. Declared primary
and white-point coordinates are retained in `RgbChromaticitiesColorDescription`.
The common `SrgbConverter` supports these coordinates with Bradford white-point
adaptation, including wide-gamut and imaginary primaries. It unassociates alpha
and performs the explicitly requested SDR conversion: nominal RGB is clipped to
0–1 before the display transform, then mapped into 8-bit sRGB. Alpha is clipped to
0–1; nonpositive alpha yields transparent black. Non-finite samples cannot be
converted into an SDR display image and cause a diagnostic. The original float
frame remains unchanged. This SDR operation is not scene-referred HDR tone mapping.

```csharp
OpenExrImageDecoder decoder = new(new() { Layer = "beauty" });
MediaBufferAllocator allocator = new(memoryBudget, MemoryPool<byte>.Shared);
using VideoFrame linear = await decoder.DecodeAsync(
    encoded, new() { PageIndex = 1 }, allocator, cancellationToken);
using VideoFrame display = SrgbConverter.Apply(linear, allocator, cancellationToken);
```

The caller owns any supplied pool. Output storage and temporary chunk storage
count against the allocator; cancellation and failure release both. No second
full-size decoded image is allocated. Scratch storage is at most twice one
uncompressed chunk; uncompressed input borrows its sample bytes directly.

## Validation and performance

Eight HALF/FLOAT × NONE/RLE/ZIPS/ZIP files were independently written by FFmpeg
with an explicit square-pixel aspect. Complete decoded float RGBA matches both
FFmpeg and OpenEXR 3.5.4, including extended-range values and transparency. The
OpenEXR reference library also writes the multipart, mixed-precision, negative
origin, named-layer, wide-gamut and non-finite fixtures. Tests cover selected
parts/layers, original values, SDR conversion, bad extents, budget exhaustion,
cancellation, UTF-8 channel ordering, contradictory layout flags and parallel use with a caller pool. Native AOT runs the same complete
pixel comparisons and color conversion.

A single-worker Linux x64 Native AOT benchmark decodes a 2048×1024 RGBA scene-linear
chart from memory. Each number is the median of 30 operations after five warmups,
pinned to one processor. Before/after use identical input, output precision and
compression; this comparison isolates vectorized byte prediction and interleaving.
The current decoder also skips initial alpha filling when an alpha channel is present.

| Source | Before | After | After CPU/op | Managed allocation/op |
| --- | ---: | ---: | ---: | ---: |
| HALF, uncompressed | 25.833 ms | 25.151 ms | 25.858 ms | 32.01 MiB |
| HALF, RLE | 71.857 ms | 35.605 ms | 35.783 ms | 32.04 MiB |
| HALF, ZIP | 69.091 ms | 32.390 ms | 32.720 ms | 32.51 MiB |
| FLOAT, uncompressed | 17.116 ms | 17.450 ms | 18.235 ms | 32.01 MiB |
| FLOAT, RLE | 16.348 ms | 16.334 ms | 17.053 ms | 32.07 MiB |
| FLOAT, ZIP | 124.685 ms | 44.328 ms | 44.312 ms | 33.01 MiB |

The float RLE chart is mostly stored as uncompressed chunks. Its unchanged timing
is expected. The output itself occupies 32 MiB in every row. With the default
allocator, cumulative allocations include a new output per operation; process
peak working sets for these series were 199–390 MiB, including the input, runtime
and garbage-collector retention. These process peaks are distinct from live buffer
budgets, which return to zero after each operation. A separate repeated series
with a caller-supplied `MemoryPool<byte>.Shared` reuses output and chunk storage:
managed allocations are 6.4–6.7 KiB per operation and process peaks are 45–76 MiB
across these six inputs. Pool retention is caller-owned and remains distinct from
active frame leases. Hardware-disabled tests also
pass; scalar byte prediction is used when vector acceleration is unavailable.

References: [OpenEXR file layout](https://openexr.com/en/latest/OpenEXRFileLayout.html),
[OpenEXR technical introduction](https://openexr.com/en/latest/TechnicalIntroduction.html),
[OpenEXR compression implementation](https://github.com/AcademySoftwareFoundation/openexr/tree/main/src/lib/OpenEXRCore).
