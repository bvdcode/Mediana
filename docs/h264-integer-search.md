H.264 integer motion search compares complete interior candidates directly with
the reference picture's rows. Only a winning candidate's luma samples are copied
to prediction storage. Implementation `9ab563b` is compared with `2327213`.

## Calculation and correctness

The search order, range, row rejection threshold, chroma comparison and motion
vector tie-breaking are unchanged. The shared 16-byte absolute-difference
calculation is applied to one source row and one strided reference row at a time.
Rejection still requires the accumulated error to exceed the current best error.
Fractional candidates and boundary extension retain their existing calculation.
No pixel ownership, public contract or allocation changes are introduced.

Shared managed and Native AOT tests compare strided differences with independent
scalar row-prefix sums. They cover four strides, 32 sample patterns, every row's
rejection boundary and exact ties, with padding and guard bytes unchanged.
The existing 188 independent complete-search cases preserve selected vectors,
all prediction samples, border handling and source ownership.

## Complete raw-video encoding

The input and settings are those of the
[stage comparison](h264-stage-performance.md#input-and-measurement): all 1,441
pictures, 60.041667 seconds, 1280x720 at 24 fps. Encoding reads the complete
1,992,038,400-byte raw reference and writes MPEG-TS; it includes frame ownership,
input and output, without source decoding, resizing or audio processing.

Frozen Native AOT binaries run serially on logical CPU 2 with
`DOTNET_PROCESSOR_COUNT=1` and no overlapping build, test or media workload.
CPU is user plus system time. These are two observations per version on a shared
Linux x64 host; they do not establish a general throughput guarantee.

| Execution order | CPU seconds | Elapsed seconds | Peak RSS, KiB |
| --- | ---: | ---: | ---: |
| before-1 | 51.936634 | 53.169268 | 50,136 |
| after-1 | 50.988886 | 52.967220 | 51,204 |
| after-2 | 50.469305 | 51.452909 | 51,892 |
| before-2 | 52.911961 | 56.006198 | 52,524 |

The CPU median decreases from **52.424298 to 50.729096 seconds, 3.23%**.
The paired reductions are 1.82% and 4.62%. Elapsed medians decrease from
54.587733 to 52.210065 seconds. RSS medians are 50.13 and 50.34 MiB;
no memory reduction is established.

Every run produces all 1,441 packets, reaches EOF and releases owned media
storage. The complete 78,332,832-byte transport matches the independently
decoded reference, SHA256
`d3090ee699cfdcc4330ec9815adaa1e7b08de1d95530e2e7af2e1596a399e543`.
Compressed samples, timing, size and decoded quality are unchanged.

## External reference and remaining work

Fresh optimized one-thread FFmpeg/libx264 processes reuse the stage comparison's
command on the same CPU, with no overlapping owned workload:

| Observation | CPU seconds | Elapsed seconds | Peak RSS, KiB |
| --- | ---: | ---: | ---: |
| FFmpeg 1 | 15.249698 | 15.421866 | 110,772 |
| FFmpeg 2 | 15.039214 | 15.251818 | 110,136 |

The ratio of CPU medians is **3.35x**: 50.729096 versus 15.144456 seconds.
Both external transports match their independently decoded reference exactly.
Their 75,383,112 bytes and 46.098 dB aggregate YUV PSNR remain better than
Mediana's 78,332,832 bytes and 45.094 dB on this input. This comparison does not
establish equal-quality throughput. Changes in external timing across batches
are not attributed to the Mediana optimization.

This measurement does not update the
[complete-film audiovisual HLS result](hls-hd-encoding.md). Fractional refinement,
coefficient writing and reconstruction remain candidates for further profiling.

## Validation

Release build with zero warnings/errors, formatting, root license-header checks,
all 2,977 managed tests and 701 H.264 tests with hardware intrinsics disabled
passed. All 30 packages were checked through an isolated package-only strict
Native AOT consumer, including full native test execution. Native CLI and sample
AOT builds also passed. Source and fixture hashes remained unchanged throughout
validation.
