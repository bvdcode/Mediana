# RGBA image resizing

`Mediana.Processing.RgbaResize.Apply` resizes a straight RGBA32 frame to explicit
pixel dimensions. `RgbaResizeOptions` selects nearest-neighbor, bilinear,
Catmull–Rom bicubic, or Lanczos3 filtering. Lanczos3 is the default.

Filtered reductions widen the reconstruction kernel by the reduction ratio on
each axis. RGB is premultiplied by alpha before either pass and unpremultiplied
only after both passes. Fully transparent output has zero RGB. Negative filter
lobes remain signed until the final output clamp.

The default `LinearSrgb` mode decodes sRGB before filtering and encodes it after
filtering. It requires sRGB input without an overriding color description.
Convert other color spaces with `SrgbConverter` first. `Encoded` explicitly
filters the original channel values and preserves their color interpretation.
Nearest-neighbor performs no color conversion. Same-size filtered operations
return an independent lease over the original pixels without copying.

Input timing, sample aspect ratio and color metadata are preserved. Requested
dimensions describe storage geometry; changing the width/height ratio stretches
the image. Padded source strides are supported. Cancellation and allocation
failures dispose all owned workspace without disposing the borrowed input.

The allocator counts output, coefficient tables, a rolling cache of horizontally
filtered rows, one decoded source row and one accumulated output row. There is
no full intermediate image. Immutable sRGB lookup tables use about 65 KiB per
process; object overhead and idle pool capacity follow the allocator contract.
`Vector4` performs the four-channel arithmetic through the runtime's SIMD path.

## Validation

Thirteen regression cases cover direct double-precision convolution, all three
filters, both color modes, enlargement, reduction, mixed-axis scaling, single-row
and single-column inputs, transparent edges, constants, padded storage, timing,
allocation failures, pre-cancellation and independent lifetime.

An independent Pillow floating-point Lanczos reference was compared after
explicit premultiplication and, where selected, sRGB linearization. Complete
RGBA comparisons differed by at most one byte level: 2 channel samples in a
193×129 photographic reduction and 16 in a 257×193 text/line/transparent-edge
reduction. Native AOT and managed output matched byte-for-byte for the latter.

Initial Linux x64 .NET 11 measurements use warmed sequential calls, Lanczos3,
synthetic opaque RGBA input, and the non-pooled allocator. Managed allocation
counts exclude input construction. Process peaks include the runtime, input and
benchmark source array; they are not codec workspace figures.

| Resize | Color mode | Median | Allocated per call | Process peak |
| --- | --- | ---: | ---: | ---: |
| 2048×1536 → 512×384 | Encoded | 49.3 ms | 1.08 MiB | 75.2 MiB |
| 2048×1536 → 512×384 | Linear sRGB | 47.7 ms | 1.08 MiB | 75.8 MiB |
| 400×300 → 1600×1200 | Encoded | 48.0 ms | 7.63 MiB | 62.4 MiB |
| 4096×3072 → 256×192 | Linear sRGB | 155.3 ms | 0.80 MiB | 141.7 MiB |
| 4096×3072 → 2048×1536 | Linear sRGB | 197.3 ms | 12.71 MiB | 213.2 MiB |
| 256×192 → 1024×768 | Linear sRGB | 16.4 ms | 3.19 MiB | 44.4 MiB |

These are local observations from a shared host, not a cross-library performance
claim. The first implementation repeated color conversion for every horizontal
tap and read vertical columns with a strided access pattern. Converting each
source row once and accumulating contiguous rows reduced the first encoded-mode
case from 92.8 to 49.3 ms with about 40 KiB more allocated workspace per call.

Filter definitions and widened reduction support were checked against
[Pillow's resampling implementation](https://github.com/python-pillow/Pillow/blob/main/src/libImaging/Resample.c).
