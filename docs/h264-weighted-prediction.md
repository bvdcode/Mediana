H.264 bidirectional sample weighting now processes 16 samples per SIMD block
using SSE2. Complete minute decoding uses 7.24% less CPU for the High/CABAC
input and 4.36% less for the Main/CAVLC input, with every output sample unchanged.

## Arithmetic and storage

The implementation follows [H.264](https://www.itu.int/rec/T-REC-H.264)
8.4.2.3, equations 8-273 and 8-276. Interleaved signed 16-bit sample/weight pairs
feed 32-bit multiply-add results. Rounding, arithmetic shifting and offset
addition happen before final saturation to the 8-bit output range. Intermediate
sums fit Int32 throughout the accepted weight and denominator domain.

Equal power-of-two weights with zero offset use the existing rounded sample
average. Smaller blocks, remaining samples and execution without SSE2 use the
existing scalar arithmetic. Length validation, rejection of partial overlap
and support for fully shared input/output storage remain in the weighting
entry point. The kernel adds no per-block heap allocation, worker tasks,
picture cache, external dependency or public API.

## Complete decoding comparison

Each operation decodes all 1,441 pictures of a 60.041667-second, 1280x720,
24 fps input and writes 1,992,038,400 visible YUV420P8 bytes. The two inputs
are the real movie excerpts used by the [High/CABAC comparison](h264-cabac-performance.md)
and the [CAVLC stage comparison](h264-stage-performance.md). They use different
bitstreams; comparisons pair before and after on each unchanged input.

Both builds use Linux x64 Native AOT, Speed compilation and the default
instruction set. All operations run serially on logical CPU 2 with
`DOTNET_PROCESSOR_COUNT=1`. Each input has before/after/after/before and
reverse-order series, four complete observations per version. No build, test
or other media operation overlaps timing. CPU is user plus system time;
RSS is peak resident process memory. The host is shared.

| Input | Version | Median CPU, s | Median elapsed, s | Median RSS, KiB |
| --- | --- | ---: | ---: | ---: |
| High/CABAC | Before | 34.100318 | 34.424825 | 62,830 |
| High/CABAC | After | 31.632262 | 31.760030 | 62,980 |
| Main/CAVLC | Before | 30.466074 | 30.509479 | 62,436 |
| Main/CAVLC | After | 29.138275 | 29.169211 | 60,910 |

High/CABAC CPU reductions are 6.88% and 7.76% in the two execution orders.
Main/CAVLC reductions are 4.65% and 3.82%. RSS varies between observations;
this change does not establish a substantial memory reduction. These isolated
decoding comparisons do not remeasure FFmpeg or complete-film HLS throughput.

Every one of the 16 complete raw outputs matches its independent reference
byte for byte. All presentation timestamps and frame durations are checked as
exact rationals; each operation reaches EOF with zero owned media bytes.
Inputs, executables and source files remain fixed throughout measurement.

## Validation

An independent floor-division oracle checks 585 weight/denominator/offset cases,
all 65,536 input byte pairs per principal case and 41,052,376 output samples.
The same checks pass in managed execution, hardware-disabled managed execution
and strict Native AOT. Cases cover signed weights, saturation, rounding,
unaligned starts, short and partial vectors, output guards, unchanged borrowed
inputs and writing over either input. Additional shared product tests cover
both inputs and output sharing storage, partial-overlap rejection and zero
allocation in repeated prediction calls.

The adopted implementation is `a51eb83`. All 3,199 managed tests and 721
hardware-disabled H.264 tests pass. Thirty locally built package assemblies,
metadata and package-only dependencies are verified; the full strict Native AOT
runner built from those packages passes. CLI and sample Native AOT builds,
full formatting, Release compilation with zero warnings and root license-header
checks also pass.


The subsequent [CABAC history comparison](h264-cabac-history.md) keeps this
weighting kernel and reduces cumulative allocation during complete decoding.
Its memory and CPU measurements are separate from the weighting result above.
