# Chroma reconstruction

Subsampled YCbCr requires reconstruction of Cb/Cr before RGB conversion. This
operation is separate from HEVC bitstream reconstruction, ICC conversion and
resizing the completed RGB image.

`Mediana.Core.Video.ChromaUpsamplingFilter` exposes four explicit kernels:

| Filter | Kernel |
| --- | --- |
| `NearestNeighbor` | One source sample; midpoint ties select the lower coordinate |
| `Bilinear` | Separable triangle, two taps on a subsampled axis |
| `Bicubic` | Catmull-Rom cubic, four taps on a subsampled axis |
| `Lanczos3` | Windowed sinc with radius three, six taps on a subsampled axis |

Larger kernels are not a universal quality ranking. They can preserve different
details and produce overshoot. Reconstruction extends source edges, retains
intermediate overshoot, then rounds and clips the final component sample.
Unsubsampled axes retain the original samples.

## HEIC

HEIC decoding defaults to `Bilinear`. Library callers select another kernel
through immutable decoder registration:

```csharp
HeicDecodeOptions options = new(ChromaUpsamplingFilter.Lanczos3);
HeicImageDecoder decoder = new(SampleAspectRatio.Square, options);
```

Both `HeicDocument` and registered decoding use this setting. The CLI uses the
bilinear default. Filtering works on native 8/10-bit components using the HEVC
sample-location type before RGB conversion. RGB conversion retains its vector
implementation. Alpha and monochrome samples are not chroma-filtered.

Each coded image is reconstructed before HEIF composition. Filters extend that
image's own edges; they do not read chroma from adjacent grid items. Geometric
transforms and auxiliary alpha composition retain their existing order.

The original four-parameter `HeicDecodeOptions` constructor remains available;
its default color reconstruction changes to bilinear. Existing nearest-neighbor
reference hashes explicitly select that filter.

## Shared processing

`Mediana.Processing.ChromaUpsampler` is an immutable plan for integer sample
planes with horizontal/vertical factors of one or two. It accepts all six
explicit sample positions and rejects an unspecified position. Byte and word
samples support 8/10-bit HEIC and other unsigned component ranges through 16 bits.

`CopyRow` borrows the source, destination and a float workspace sized by
`WorkspaceLength`. Calls allocate no buffers. Source indices scale with axis
lengths; kernel weights are stored once per repeating phase, at most two phases
per axis. HEIC temporary rows scale with coded image width.
No full-resolution intermediate Cb/Cr planes or additional full RGBA copy are
created. Plans can be shared concurrently with separate caller workspaces.

## Verification and comparisons

- Independent Pillow 12.3.0 float-plane references cover all four kernels at
  8- and 10-bit ranges. Cubic/Lanczos comparisons exclude the boundary where
  Pillow normalizes truncated kernels instead of extending edge samples.
- Separate checks cover sample positions, padded rows, edge extension, clipping,
  constant planes, cancellation, validation and allocations during repeated rows.
- The 42 original HEIC files retain their nearest-neighbor RGBA hashes. All four
  filters exercise visible geometry and alpha; prof/rICC profiles, copied/direct
  frame ownership and common ICC conversion are checked for every filter.
- The same numerical and HEIC checks execute with Native AOT.

The [2026-11-06 comparison](performance/results/2026-11-06/README.md) explicitly
uses nearest-neighbor for both engines. Those results describe that source
snapshot and that mode, not bilinear decoding speed. The standalone HEIF callers
now accept an explicit filter argument and print the selected mode. libheif's
caller supports `nearest` and `bilinear`; Mediana also supports `bicubic` and
`lanczos3`. Both callers default to `bilinear`.

Sample position, edge treatment, component precision and output color processing
must also agree before timing results establish an advantage at equal quality.
In particular, libheif 1.23.2's bilinear implementation assumes centered samples;
its output is not an interchangeable reference for every HEVC sample position.
