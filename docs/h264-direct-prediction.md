# Direct prediction in H.264 B pictures

Direct prediction derives both reference indices and motion vectors before
calculating predicted pixels. When every subpartition of a region has the same
L0 and L1 motion, Mediana calculates its pixels as one 8×8 or 16×16 region.
Nonuniform regions keep their individual 4×4 or 8×8 calculations. Both paths
write the same reference indices and vectors for every constituent 4×4 block.

B_Skip and B_Direct_16x16 enter this process once per macroblock. Direct
subpartitions in a mixed B_8x8 macroblock enter it separately. Spatial neighbor
prediction is shared within the region; co-located zero decisions remain local
to each subpartition. Temporal reference mapping, explicit and implicit weights,
interpolation, border extension, rounding and clipping are preserved.

[H.264](https://www.itu.int/rec/T-REC-H.264) clause 8.4.1.2.2, notes 1 and 3,
describes the shared spatial neighbor inputs and predicted motion. Clause
8.4.1.2.3, note 3, permits calculating sample values in larger regions when
subpartitions share their motion and reference pictures.

## Complete HD decoding

A Native AOT consumer decodes the complete 60.041667-second 1280×720 source
described in [the stage comparison](h264-stage-performance.md), selects all
1,441 pictures and writes every visible YUV row. Each output contains
1,992,038,400 bytes and matches independent decoding byte for byte. Every
presentation timestamp matches the preceding implementation, and all owned
media storage is released.

A separate complete-input diagnostic run counts 2,024,648 direct regions,
including 1,958,730 uniform complete macroblocks. B-partition pixel prediction
calls fall from 8,434,151 to 2,557,961. Total luma motion-compensation calls fall
from 18,907,909 to 7,888,408, and chroma calls from 37,815,818 to 15,776,816.
These counts verify reduced work; the timing observations below use binaries
without this diagnostic instrumentation.

Two series run serially in before/after/after/before and
after/before/before/after order. Each process uses logical CPU 2 of the same
Linux x64 host, .NET SDK 10.0.402 and `DOTNET_PROCESSOR_COUNT=1`. No other build,
test or media operation from this comparison overlaps measurement. CPU is user
plus system time; RSS is peak process resident memory.

| Series | Version | CPU, seconds | Elapsed, seconds | RSS, MiB |
| --- | --- | ---: | ---: | ---: |
| First, 1 | Before | 39.827 | 46.247 | 71.25 |
| First, 2 | After | 26.669 | 27.441 | 72.81 |
| First, 3 | After | 24.180 | 24.803 | 72.21 |
| First, 4 | Before | 28.094 | 28.984 | 71.72 |
| Reverse, 1 | After | 22.030 | 23.513 | 71.58 |
| Reverse, 2 | Before | 27.035 | 27.614 | 69.94 |
| Reverse, 3 | Before | 26.060 | 26.190 | 74.96 |
| Reverse, 4 | After | 21.403 | 21.872 | 70.32 |

The median of four observations per version is 27.565 → 23.105 seconds CPU
(16.18% less) and 28.299 → 24.158 seconds elapsed (14.63% less). Median peak RSS
is 71.48 → 71.90 MiB; this change does not establish a memory reduction.
The first baseline observation is substantially slower than the remaining
baseline observations and is included in the table and medians. These are
observations on one host and one complete input.

Two fresh FFmpeg processes decode the same complete input with one decoder,
output and filter thread and the same CPU affinity. Every output byte matches.
Their median CPU time is 4.243 seconds versus 23.105 for the public Mediana
consumer, a 5.45x difference in this batch. The paired before/after observations
above establish the improvement; the different FFmpeg time in earlier batches
is not part of that improvement.

The subsequent [complete-film HLS measurement](hls-hd-encoding.md) includes this
implementation: 962.836 seconds CPU versus 274.925 for FFmpeg on the complete
596.458333-second audiovisual ladder. All 406 output files remain identical to
the independently verified references. Mediana CPU is 3.03% lower than the
preceding full-film observation; the external baseline is 39.16% higher. This
single full-operation comparison does not isolate the direct-region change or
establish the same percentage improvement as the paired decoder measurement.

## Verified contracts

Shared managed checks cover spatial and temporal direct prediction, both
inference sizes, uniform and nonuniform motion, short-term and long-term
references, absent co-located reference indices, all reference-list utilization
combinations, and ordinary, explicit and implicit weighting. They compare every
predicted pixel, both complete motion lists and block modes with independently
derived motion and separate subpartition calculations. Pixels outside a
selected 8×8 region remain unchanged, and picture disposal releases all media
storage. The same checks pass without hardware intrinsics.

Release compilation, formatting, license headers, 2,977 managed tests and 701
H.264 checks without hardware intrinsics pass. The same shared direct-region
checks pass in a strict Native AOT consumer that references only the 30 built
packages. Native AOT compilation of the sample and CLI also passes.

All four CFR, VFR, edited and HD full transcodes and their selected windows
remain byte-identical in managed and Native AOT execution. Independent decoding
verifies every output picture and its original presentation time. Transcoding
the original film's 540–546 second interval and CLI PNG previews of cropped and
HD inputs also preserve the previous complete bytes.
