H.264 deblocking at `f46ecc0` recognizes uniform prediction within a
macroblock. For valid inter prediction, if the active reference indices and motion components are equal
across all sixteen 4x4 cells, internal inter boundaries without residual
coefficients have strength zero. The calculation checks this invariant once
per orientation fill and validates its active physical references before reuse.
Intra and residual strength take precedence; macroblock boundaries retain
their physical-reference and motion comparison.

The scope of reuse ends with each synchronous fill. Later mutations of motion
or reference-index spans are observed by the next call. The full Int32 motion
domain, unavailable-reference errors and inactive-list semantics are preserved.
This introduces no frame buffer, persistent motion cache or processing worker.
Four-component equality uses the .NET
[Vector128 operations](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.intrinsics.vector128.equalsall?view=net-10.0); the same checks
also pass with hardware intrinsics disabled.

## Complete operation comparison

The unchanged baseline is `4f0da70`. Each serial series runs baseline, current,
current, baseline on logical CPU 2, with one media-processing thread,
`DOTNET_PROCESSOR_COUNT=1`, ordinary GC and default Native AOT compiler settings.
CPU is process user plus system time. Values below are medians of the two
complete observations per implementation on Linux x64.

| Operation | Before CPU, s | Current CPU, s | Observed CPU change |
| --- | ---: | ---: | ---: |
| High/CABAC decode, 180.125 s | 53.843 | 49.386 | -8.28% |
| CAVLC decode, 60.042 s | 18.002 | 17.110 | -4.95% |
| H.264 encode, 60.042 s | 34.482 | 35.162 | +1.97% |

| Operation | Before elapsed, s | Current elapsed, s | Before peak RSS, MiB | Current peak RSS, MiB |
| --- | ---: | ---: | ---: | ---: |
| High/CABAC decode | 75.664 | 61.811 | 68.70 | 80.66 |
| CAVLC decode | 20.727 | 18.658 | 67.50 | 68.00 |
| H.264 encode | 36.079 | 38.295 | 36.10 | 38.21 |

The High/CABAC input contains 4,323 pictures at 1280x720/24 fps. It repeats
the independently checked 60.041667-second source three times with continuous
packet timestamps; it is not a separate three-minute scene. All four decodes
write 5,976,115,200 bytes with SHA256
`8a2f459cc105b3a2a0f6239c5be0e0341bd4551317812544bf2fadcc5649a627`.
Every frame format, rational timestamp and duration, EOF and release of owned
media storage is checked.

The CAVLC input and encoder settings remain those of the
[complete stage comparison](h264-stage-performance.md). All four CAVLC decodes
write the same complete 1,992,038,400-byte reference. All four encodes produce
78,332,832 bytes with SHA256
`d3090ee699cfdcc4330ec9815adaa1e7b08de1d95530e2e7af2e1596a399e543`,
preserving the previously independently decoded bitstream, timing and quality.

Absolute times and RSS vary on the shared host. The complete High/CABAC
series shows lower CPU; encoding in this series uses slightly more CPU.
RSS increases in the High/CABAC series, and these observations establish no
general memory reduction. Steady-state classification itself allocates zero
managed bytes in the shared native and managed checks.

## Fresh external decoder comparison

A separate FFmpeg, Mediana, Mediana, FFmpeg series uses the original complete
60.041667-second High/CABAC input: 1,441 pictures and 1,992,038,400 output
bytes per operation. Both implementations use their ordinary instruction-set
selection, one media-processing thread and the same logical CPU. Every output
matches the independent raw reference; Mediana also checks all frame clocks,
EOF and released media storage.

| Decoder | Median CPU, s | Median elapsed, s | Median peak RSS, MiB |
| --- | ---: | ---: | ---: |
| Mediana Native AOT | 16.964 | 18.416 | 71.08 |
| FFmpeg 6.1.1 | 3.355 | 4.317 | 71.92 |

The current CPU ratio is 5.06x. These are fresh
complete-decoding measurements. They do not update full HLS throughput or
establish equal-quality encoding speed. The substantial CPU gap and the
[complete-film encoding efficiency difference](hls-hd-encoding.md) remain open.

## Correctness and validation

Independent controls cover 264,192 intra/residual topology patterns and 93,312
physical-reference/motion cases, including aliases, swapped lists and extreme
Int32 values. Shared tests add 31,248 uniform-prediction and every-cell mutation
patterns, internal-only missing references, reused mutable contexts, output
guards, inactive-list values and zero steady-state allocation.

Release build, all 3,204 managed tests, 725 H.264 tests with hardware intrinsics
disabled, all 30 packages, strict package-only Native AOT execution, native CLI,
managed sample publication, formatting and root license-header checks passed.
Independent calculation checks also pass in managed, software-managed and strict Native AOT
execution. Codec settings, output samples and encoded bytes are unchanged.
