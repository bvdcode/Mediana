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

## Whole-file measurements

The [raw runs and output hashes](chroma-reconstruction-results.json) and
[median table](chroma-reconstruction-results.csv) compare `92dc2f3` with phase-table
reuse in `f441f05`. Both builds use Release Native AOT, .NET SDK 10.0.403 and an
Intel Core i5-12450H. Each mode runs in three fresh processes on logical CPUs 2
and 4 with `DOTNET_PROCESSOR_COUNT=2`; execution order reverses on alternating
repetitions. Input cache is warm and output is written to tmpfs. Timing includes
startup, complete HEIC decoding, full source-color RGBA8 output and process exit.
ICC conversion and PNG compression are outside these measurements.

Final Mediana medians:

| Image | Filter | Time | Memory |
| --- | --- | ---: | ---: |
| 4000×3000 | Nearest | 996ms | 68Mb |
| 4000×3000 | Bilinear | 1117ms | 69Mb |
| 4000×3000 | Bicubic | 1226ms | 69Mb |
| 4000×3000 | Lanczos3 | 1275ms | 69Mb |
| 16320×12240 | Nearest | 9734ms | 875Mb |
| 16320×12240 | Bilinear | 11159ms | 872Mb |
| 16320×12240 | Bicubic | 11946ms | 875Mb |
| 16320×12240 | Lanczos3 | 13225ms | 873Mb |

These runs share a development host without exclusive CPU or frequency control.
The unchanged nearest path also changes timing between the before/after stages;
the timings do not isolate a speedup caused by phase-table reuse. Peak RSS
is process memory, not total managed allocation, and small RSS differences do
not establish a memory ordering between kernels.

Allocation for one reusable 256×256-to-512×512 plane plan, measured over 100
constructions after warm-up, changes deterministically:

| Filter | Before, bytes | After, bytes |
| --- | ---: | ---: |
| Nearest | 8416 | 4336 |
| Bilinear | 12512 | 4352 |
| Bicubic | 20704 | 4384 |
| Lanczos3 | 28896 | 4416 |

These counts cover plan objects and arrays, not decoded image storage. HEIC's
optimized nearest path does not construct a plan. Row processing allocates zero
additional managed buffers when supplied with its caller-owned workspace.

All eight complete Mediana outputs, covering both photographs and all four
filters, retain exactly the same SHA-256 before and after phase-table reuse.
Nearest output differs from libheif/libde265 by at most one RGBA code value on
both complete photographs, with RGBA RMS errors of 0.139705 and 0.131672. The
paired native nearest baseline remains faster. Bilinear equivalence to that native
version is not established because sample-location and edge behavior differ;
those results are not presented as an equal-quality speed comparison.
