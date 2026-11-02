The H.264 search and interpolation comparison uses the complete 1,441-picture,
60.041667-second, 1280x720 input from the
[stage performance comparison](h264-stage-performance.md). The implementation
is `19e2c6b`, measured against `64b8d2b`. Frozen Native AOT consumers and FFmpeg
run serially on one assigned CPU, with one processing thread. CPU is user plus
system time; RSS is peak process resident memory. These are observations on a
shared Linux x64 host, not cross-platform throughput guarantees.

## Search work

A separate instrumented encoder processed 5,148,000 P-picture macroblocks.
Its complete output retained the existing encoded reference bytes.

| Candidate stage | Evaluations | Previously evaluated positions |
| --- | ---: | ---: |
| Integer search and rounded predictor | 36,041,521 | 4,777,794 |
| Half- and quarter-sample neighborhoods | 80,417,584 | 0 |
| Final original predictor | 2,708,876 | 1,773,794 |

Instrumented picture encoding took 62.761 elapsed seconds, including 28.426 in
motion search, 5.931 in integer search and 19.945 in fractional refinement.
These nested timings include instrumentation overhead and are not CPU
percentages. Duplicate integer evaluations alone do not explain the gap;
substantial work also remains outside motion search.

[x264's motion estimation](https://github.com/mirror/x264/blob/master/encoder/me.c)
uses staged refinement, direct reference comparisons and batched candidate
costs. Its [analysis](https://github.com/mirror/x264/blob/master/encoder/analyse.c)
selects when to probe skipped macroblocks. A selective early-skip experiment in
Mediana reduced full-sequence CPU from 61.696 to 58.463 seconds, but aggregate
PSNR fell from 45.093622 to 45.039256 dB while transport size fell only 0.013%.
The experiment was rejected; production search decisions remain unchanged.

## Direct chroma reference access

Chroma interpolation previously copied every source tile into a temporary patch,
including tiles entirely inside the reference picture. Each interior 8x8
prediction therefore copied 9x9 samples per chroma plane before interpolation.

Interior tiles now read the reference span with its original row stride. Tiles
requiring border extension retain the existing bounded patch. The bounds include
the additional right and lower interpolation samples. Rounding, candidate order,
tie decisions and vector arithmetic are unchanged; no heap storage is added.

| Order | Version | CPU seconds | Elapsed seconds | RSS, KiB |
| --- | --- | ---: | ---: | ---: |
| 1 | Before | 60.947296 | 61.556647 | 52,904 |
| 2 | After | 58.973950 | 59.919306 | 50,936 |
| 3 | After | 59.564777 | 61.895264 | 52,588 |
| 4 | Before | 62.653766 | 64.199603 | 52,556 |

Paired CPU reductions are 3.24% and 4.93%. RSS varies in opposite directions.
Every complete output retains SHA256
`d3090ee699cfdcc4330ec9815adaa1e7b08de1d95530e2e7af2e1596a399e543`;
encoding decisions, size and independently verified decoded quality are
unchanged. All processes reach EOF with zero owned media bytes.

Two fresh optimized FFmpeg/libx264 encodes with the original `veryfast` settings
use 13.141467 and 13.739693 CPU seconds, 13.272453 and 13.759789 elapsed seconds,
and 112,196 and 115,900 KiB RSS. Both outputs match the independently decoded
FFmpeg reference. The ratio of encoder CPU medians remains **4.41x**. This is
not equal-quality throughput: FFmpeg's reference remains smaller and has higher
PSNR, as documented in the stage comparison.

## Shared decoder check

The interpolation kernel is also used by decoding. Each complete decode writes
1,992,038,400 raw bytes, all identical to the independent reference. The native
processes reach EOF and release their media storage.

| Implementation | CPU seconds | Elapsed seconds | RSS, KiB |
| --- | ---: | ---: | ---: |
| Before | 32.411720 | 32.898191 | 107,252 |
| After | 32.264707 | 35.706068 | 93,300 |
| FFmpeg | 3.820731 | 5.746038 | 75,376 |

The single pair does not establish a decoder speed improvement: CPU differs by
less than 0.5%, and elapsed time increases. Mediana still uses **8.44x** the
FFmpeg decode CPU. This operation is decoding to raw samples, not transcoding.
The full-film audiovisual HLS comparison has not been repeated for this change.

## Validation

Release build, all 2,956 managed tests, 695 H.264 tests with hardware intrinsics
disabled, all 30 package checks, strict package-only Native AOT execution,
native CLI publication, formatting and license headers pass. Shared tests cover
every chroma fraction, interior and border transitions, partial tiles, extreme
coordinates, output guards and independent scalar sample calculations. Existing
motion-search reference cases retain exact candidate decisions.
