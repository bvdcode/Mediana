H.264 luma interpolation selects the fractional-sample calculation once per
block and processes four or eight samples together. Implementation `ec76d3b`
is compared with the Native AOT binary from `13ecd6d`, following the
[block quantization change](h264-block-quantization.md).

## Computation and correctness

The six-tap filter, unclipped diagonal intermediate and clipped quarter-sample
averaging follow [ITU-T H.264](https://www.itu.int/rec/T-REC-H.264-202606-I/en),
clause 8.4.2.2.1 and Table 8-12. The
[FFmpeg block interpolation implementation](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/h264qpel_template.c)
provides a comparison for dispatching by block size and phase. Mediana retains
its own implementation and shared encoder/decoder filter calculations.

Horizontal, vertical, odd/odd and diagonal phases perform only their required
calculations. Only diagonal phases allocate the bounded horizontal workspace. Border
blocks use a bounded source patch. Intermediate storage is on the stack, and results are
written to the caller's output. Four-sample vectors read and write exactly four bytes;
one-to-three-sample tails retain the same scalar equations.

Rounded byte averaging is shared with encoder motion search. Supported x86
processors use SSE2 PAVGB; the portable vector equation remains available.
Native x64 disassembly confirms separate four/eight-byte reads, vector filter
multiplication, PAVGB and inlined sample-loading helpers with no runtime
four/eight-width branch inside a load.

The preceding diagnostic decode counted 18,907,909 luma predictions and
7,726,869 fractional interpolation calls. Fractional calls used 8-pixel blocks
5,970,211 times and 16-pixel blocks 1,756,658 times. Instrumented elapsed times
were 7.770 seconds for luma prediction and 6.697 for its interpolation subset.
These nested timers include instrumentation overhead and are not CPU percentages.
Four-pixel blocks and irregular tails are covered by correctness tests; this
input does not establish their individual throughput.

## Complete decoding and encoding

The input, stage definitions and codec settings are those of the
[stage comparison](h264-stage-performance.md#input-and-measurement):
1,441 pictures, 60.041667 seconds, 1280x720 at 24 fps. Every operation reaches
EOF. Native AOT binaries run serially on one assigned CPU, with one
media-processing thread and no overlapping build, test or media workload.
CPU is user plus system time. Observations are from a shared Linux x64 host.

Decoding includes demuxing and writing all 1,992,038,400 raw YUV bytes:

| Execution order | CPU seconds | Elapsed seconds | Peak RSS, KiB |
| --- | ---: | ---: | ---: |
| before-1 | 32.514667 | 32.972617 | 102,404 |
| after-1 | 29.157130 | 29.867756 | 89,552 |
| after-2 | 28.247209 | 31.788930 | 106,684 |
| before-2 | 32.590801 | 37.015765 | 91,644 |

Paired CPU reductions are **10.33%** and **13.33%**.
All decoded samples match the independent reference, SHA256
`1ae555fcff80cdedd1f41a1d1b9aa86aadcb23884b1e11305d6c1ab5191d2bca`.

Encoding reads that complete raw reference and writes the MPEG-TS stream:

| Execution order | CPU seconds | Elapsed seconds | Peak RSS, KiB |
| --- | ---: | ---: | ---: |
| before-1 | 48.927933 | 51.286629 | 51,912 |
| after-1 | 45.117257 | 46.313778 | 52,656 |
| after-2 | 46.793071 | 48.257338 | 51,820 |
| before-2 | 48.573708 | 49.449016 | 50,932 |

Paired CPU reductions are **7.79%** and **3.67%**.
All encoded bytes match the independently decoded reference, SHA256
`d3090ee699cfdcc4330ec9815adaa1e7b08de1d95530e2e7af2e1596a399e543`.
Size, timing and decoded quality are unchanged. Every Mediana operation
finishes with zero owned media bytes. Peak RSS varies between observations;
these results do not establish a general memory reduction.

## Remaining comparison with FFmpeg

Fresh optimized one-thread FFmpeg/libx264 processes use the original settings:

| Stage and observation | CPU seconds | Elapsed seconds | Peak RSS, KiB |
| --- | ---: | ---: | ---: |
| Decode 1 | 3.289344 | 3.289960 | 64,144 |
| Decode 2 | 3.678129 | 3.679477 | 66,680 |
| Encode 1 | 14.302091 | 15.756476 | 113,564 |
| Encode 2 | 14.133038 | 15.630726 | 112,608 |

The ratio of CPU medians is **8.24x for decoding**
(28.702 versus 3.484 seconds) and
**3.23x for encoding**
(45.955 versus 14.218 seconds). The CPU gap remains.
All external decoded samples and encoded bytes match their independent
references. The encoding comparison is not equal-quality throughput:
FFmpeg's reference remains smaller with higher PSNR. External timing varies
between batches, so the change's effect is measured by the paired native
observations above. No new complete-film audiovisual HLS timing is established.

## Validation

Shared managed and Native AOT checks
cover all 16 phases, 14 block shapes, every border, extreme coordinates,
independent scalar convolution, source/output guards and all 65,536 pairs of
byte values for rounded averaging. Existing H.264, encoding and HLS references
remain unchanged.
