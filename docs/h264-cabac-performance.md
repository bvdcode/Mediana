At baseline `e96eb92`, complete High-profile CABAC decoding was measured separately from the existing
[CAVLC stage comparison](h264-stage-performance.md). Mediana uses 34.345 CPU
seconds versus FFmpeg's 5.204 seconds for this input, a 6.60x gap. Every decoded
sample and every Mediana presentation timestamp and duration is exact.

[The current independent comparison](#current-independent-decoder-comparison)
updates this baseline after the direct reference/motion change.

## Input and controls

The source contains 1,441 pictures at 1280x720 and 24 fps, lasting 60.041667
seconds. It was encoded from the same movie excerpt using libx264 `veryfast`,
CRF23, High profile, YUV420P8, a 144-picture key-frame interval, disabled
scene-cut keyframes and one encoding thread. The bitstream includes B pictures,
weighted prediction and 8x8 transforms. Independent PPS inspection confirms
`entropy_coding_mode_flag=1` and `transform_8x8_mode_flag=1`; profile alone does
not establish entropy coding. A time-code data track is present; there is no
audio track. Source SHA256:
`d973ecbbf7b76ae83d046df8e91ba627ed0dcd1abf7e1d4f3819dd5a25cc7fea`.

Both decoders demux the complete source and write all 1,992,038,400 visible
YUV420P8 bytes. The Mediana consumer checks each timestamp and duration using
exact rational comparisons, reaches EOF and releases all owned media storage.
The independently decoded raw reference has SHA256
`d4da435776e1e1fd96f1abe17558d17e7c69b5b81affdf5637582ff2908178f6`.

The builds use .NET SDK 10.0.402, Linux x64 Native AOT, Speed compilation and the
default instruction set. Measurements use logical CPU 2, one media-processing
thread, `DOTNET_PROCESSOR_COUNT=1` and sequential FFmpeg/Mediana/Mediana/FFmpeg
execution. No concurrent build, test or media operation runs during timing.
CPU means user plus system time; RSS is peak resident process memory. The host
is shared. These are complete decoding measurements, distinct from encoding,
audiovisual HLS and comparisons using the different CAVLC bitstream.

| Decoder | Observation | CPU, s | Elapsed, s | RSS, KiB |
| --- | --- | ---: | ---: | ---: |
| FFmpeg | 1 | 5.212634 | 5.217432 | 69,244 |
| Mediana | 1 | 34.115281 | 35.078897 | 62,952 |
| Mediana | 2 | 34.574531 | 37.074266 | 62,844 |
| FFmpeg | 2 | 5.195218 | 5.196980 | 71,832 |
| Mediana | Median | 34.344906 | 36.076581 | 62,898 |
| FFmpeg | Median | 5.203926 | 5.207206 | 70,538 |

Mediana's median RSS is 10.83% lower. The CPU gap remains substantial.
All four complete outputs match the independent raw reference byte for byte.
The Native AOT consumer builds with warnings treated as errors. Product code
is unchanged from the 3,198-test managed and strict Native AOT validation.

## Native stack observations

Two additional complete ordinary decodes surround two complete sampled decodes
of the same binary and source. Every output, timestamp and duration remains
exact. Ordinary CPU median is 33.969998 seconds; sampled CPU median is
34.070522 seconds. Sampling increases elapsed time from a 34.719039-second
ordinary median to 46.438093 seconds, so competitive timing uses only ordinary
operations.

There are 514 selected stack points: 152 in deblocking, 90 in prediction,
32 in CABAC arithmetic/context handling, 19 in inverse transformation,
10 in CABAC residual syntax, two in container/intra work and 209 elsewhere.
Selection chooses the thread with the largest positive CPU-tick change at each
stop. Waiting and runtime stacks remain included. These counts are diagnostic
observations rather than CPU percentages.

Deblocking reference resolution and strength classification have 40 and 35
leaf points respectively. Bidirectional weighting has 28; motion updates have
21. The decoder's next useful targets include metadata classification and
prediction kernels. The presence of CABAC alone does not justify assigning
the entire gap to entropy decoding or introducing slice workers.

## Resolved-motion reuse control

A separate fixed-size cache retains the two resolved reference/motion samples
for each of 16 current, four left and four top 4x4 blocks while classifying a
macroblock. It preserves physical reference identity, residual/intra priority,
list ordering and slice boundaries. Its 24 entries occupy 768 stack bytes per
deblocking range; it creates no per-macroblock arrays.

Four complete before/after/after/before Native AOT decodes preserve every raw
sample, timestamp and duration, EOF and zero owned media. CPU median changes
from 33.800783 to 32.158611 seconds, a 4.86% reduction. RSS medians are 62,950
and 63,020 KiB. This first series falls short of the predeclared 5% minimum CPU
reduction. The variant is not adopted; the remaining CAVLC and reverse-order
series are not run. The ordinary decoder and its storage contracts are
unchanged.

The subsequent [SIMD weighting comparison](h264-weighted-prediction.md) reduces
complete decoding CPU on the same CABAC input in both execution orders. It
measures a separate change and does not replace the ordinary FFmpeg comparison
above.


## Internal-index comparison control

A separate internal-edge shortcut checks equal reference indices and close
motion vectors within a macroblock before resolving full physical identities.
Intra/residual priority, swapped or duplicate references, unavailable entries
and slice edges remain independently checked in 93,312 cases in managed,
hardware-disabled and strict Native AOT execution.

Four complete CABAC decodes preserve every raw sample, timestamp and duration,
EOF and zero owned media. CPU median changes from 20.870151 to 20.426722 seconds,
a 2.13% reduction. This series does not meet its predeclared 5% threshold, so
the shortcut is not adopted and no reverse-order or CAVLC extension is run.
These are paired results on a shared host, not an update to the FFmpeg ratio.

The subsequent [CABAC history comparison](h264-cabac-history.md) reduces
auxiliary syntax storage from the complete picture to two macroblock rows.
Complete CABAC allocation traffic falls 15.35%; no sustained CPU improvement
is established for that change.


## Direct reference and motion comparison

At baseline `172129f`, four complete ordinary/sample/sample/ordinary Native AOT
decodes repeat the High/CABAC input and all exact sample, presentation-time,
duration, EOF and released-storage checks. Ordinary CPU median is 23.442 seconds;
ordinary elapsed median is 26.991 seconds. Sampled elapsed median is 33.180 seconds
and is not used as competitive throughput. Of 359 selected stack points,
101 are in deblocking, 54 in prediction, 29 in CABAC arithmetic/context work,
11 in inverse transforms, nine in residual syntax, three in intra prediction,
one in the container and 151 elsewhere. These counts are observations, not CPU
percentages.

Boundary strength now resolves physical picture identities before comparing
motion components. It avoids constructing and passing four reference/motion
tuples for each inter edge. A matching list order returns immediately; swapped
lists are checked when needed. Intra and residual priority, unavailable-reference
errors, inactive lists and wide subtraction at the motion threshold are unchanged.
The implementation removes the separate tuple comparison helper; no cache,
media allocation, pixel copy, worker or public API is added.

Physical reference identity and the quarter-sample threshold follow
[ITU-T H.264](https://www.itu.int/rec/T-REC-H.264-202606-I/en), clause 8.7.2.1.
The current [FFmpeg loop-filter implementation](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/h264_loopfilter.c)
was inspected as an independent comparison of reference and motion classification.
Mediana retains its own implementation. Native disassembly shows the identity
resolver body changing from 702 to 486 bytes and the strength classifier's
explicit stack reservation from 104 to 40 bytes. The classifier body grows from
656 to 1,352 bytes through inlining; this is not a native code-size reduction.

### Complete paired decoding

Both inputs contain 1,441 pictures over 60.041667 seconds at 1280x720/24 fps.
CABAC and CAVLC before/after binaries run serially on logical CPU 2, with one
media thread, `DOTNET_PROCESSOR_COUNT=1` and the ordinary product GC policy.
Every decode writes and independently hashes all 1,992,038,400 YUV bytes, checks
every rational presentation time and duration, reaches EOF and releases owned
media storage. Execution order is reversed for the second series of each input.

| Input and order | Before CPU, s | After CPU, s | CPU change |
| --- | ---: | ---: | ---: |
| CABAC: before, after, after, before | 21.895 | 20.182 | -7.82% |
| CABAC: after, before, before, after | 21.649 | 20.460 | -5.49% |
| CAVLC: before, after, after, before | 20.401 | 20.388 | -0.06% |
| CAVLC: after, before, before, after | 18.880 | 18.131 | -3.97% |

All 16 complete outputs remain exact. CABAC passes the predeclared minimum 5%
CPU reduction in each order; neither CAVLC series regresses. The overall CABAC
CPU medians are 21.895 and 20.182 seconds. CAVLC overall medians differ by only
0.78%, so these results do not establish a sustained CAVLC speedup. CABAC RSS
medians are 61,824 and 65,384 KiB; allocation traffic is essentially unchanged.
No memory reduction is claimed for this computation change.

### Current independent decoder comparison

A separate FFmpeg 6.1.1/Mediana/Mediana/FFmpeg 6.1.1 control uses the complete High/CABAC
input, raw output, CPU affinity and one-media-thread settings above. This is
complete decoding and output, distinct from encoding or audiovisual HLS.

| Decoder | Observation | CPU, s | Elapsed, s | Peak RSS, KiB |
| --- | --- | ---: | ---: | ---: |
| FFmpeg | 1 | 3.846 | 4.032 | 67,956 |
| Mediana | 1 | 20.032 | 21.142 | 69,808 |
| Mediana | 2 | 18.890 | 19.544 | 61,740 |
| FFmpeg | 2 | 3.829 | 4.052 | 77,084 |
| FFmpeg | Median | 3.838 | 4.042 | 72,520 |
| Mediana | Median | 19.461 | 20.343 | 65,774 |

Every complete raw output matches the independent reference. The CPU-median
ratio is **5.07x**; the substantial gap remains. RSS median is 9.30% lower in
this series, with visible observation-to-observation variation. These shared-host
results do not establish behavior on other hardware, fresh whole-film HLS
throughput or a causal comparison against the earlier FFmpeg series.

### Validation

The independent 93,312-case matrix covers live and captured lists, reordered
slice references, duplicate identities, list swaps, inactive vectors, missing
references, both edge orientations, internal and macroblock edges, and extreme
signed motion components. Strict zero-allocation and macroblock-reset checks
also pass. The matrix runs in managed, hardware-disabled and strict Native AOT
execution, together with existing bidirectional and multi-slice checks.

Release build without warnings or errors, all 3,202 managed tests, 723 H.264 tests
without hardware intrinsics, all 30 package checks, the complete package-only
strict Linux x64 Native AOT runner, native CLI, sample build, formatting and root
license-header checks pass. This change does not introduce a concurrency model
or close the remaining encoding, HLS, quality and application acceptance work.

### Profile after direct reference comparison

Four complete decodes repeat the unchanged implementation after the direct
reference comparison: ordinary, sampled, sampled, ordinary. All 1,441 pictures,
raw output bytes, rational presentation times and durations remain exact;
EOF and released-storage checks pass. CPU affinity, one media thread and
ordinary GC settings are unchanged.

The ordinary runs use 19.722 and 20.007 CPU seconds, with a median of 19.864.
Their elapsed times are 25.421 and 20.486 seconds. Sampled elapsed median is
27.396 seconds and is excluded from competitive timing.

The two sampled runs provide 291 selected stack points: 72 in deblocking,
47 in prediction, 20 in CABAC arithmetic/context work, four in inverse
transforms, four in intra prediction, two in residual syntax and 142 elsewhere.
The remaining group includes 32 stacks through motion metadata reads or writes,
19 with raw-file writing as the innermost frame and 19 with thread yielding.
These stop counts are not CPU percentages or proof of the full decoder gap's
cause. They identify boundary classification and motion metadata access as
remaining areas to investigate alongside pixel prediction. No computation is
changed by this profile; the independent FFmpeg comparison above remains the
current paired comparison.

## Captured reference representation control

A separate control at baseline `f52d998` uses the existing captured physical
reference identities for single-slice filtering as well as multi-slice filtering.
Single-slice decoders capture their already required identity arrays before
filtering. This removes the filter's live-picture-list branch without adding a
snapshot, buffer, cache, worker or public API.

The complete High/CABAC input, output verification, CPU affinity, one-thread
settings and ordinary GC policy above are retained. The first series produces:

| Version | Observation | CPU, s | Elapsed, s | Peak RSS, KiB |
| --- | --- | ---: | ---: | ---: |
| Current | 1 | 19.629 | 22.017 | 69,452 |
| Captured only | 1 | 20.376 | 26.505 | 62,080 |
| Captured only | 2 | 19.496 | 22.810 | 72,072 |
| Current | 2 | 19.347 | 20.965 | 61,756 |

CPU medians are 19.488 and 19.936 seconds: the candidate is 2.30% slower in this
series. RSS medians are 65,604 and 67,076 KiB; no memory improvement is established.
All four complete 1,441-picture outputs, rational timestamps and durations are
exact; EOF and released media storage checks pass. The independent 46,656-case
captured-reference matrix, missing-reference, reset and zero-allocation controls
pass in managed, hardware-disabled and strict Native AOT execution.

The candidate fails the predeclared minimum 5% CPU reduction and is not adopted.
Reversed-order and CAVLC extensions are not run. This single series does not
establish a sustained regression or a cause for the wider decoder gap. The
current filter retains both live and captured reference inputs; its previous
complete validation and independent FFmpeg comparison remain separate results.

## Grouped arithmetic normalization control

A control at baseline `b6fbbba` replaces the per-bit normalization loop with a
bounded shift count and one call to the existing checked bit reader. The shift
count is `8 - floor(log2(range))` for an arithmetic range below 256. The reader
continues validating emulation prevention and truncation; normalized offsets
retain the H.264 clause 9.3.3.2.2 bound. No input lookahead, arithmetic state,
media buffer, worker or public API is added.

Independent arbitrary-precision intervals and scalar bit shifts agree on
32,768 decisions and 81,920 false termination transitions across all initial
probability states, both most-probable symbols, every byte alignment, all four
range columns and normalization lengths zero through seven. Damaged EBSP and
truncated-input errors retain the consumed bit position. These checks and strict
zero-allocation repetition pass in managed, hardware-disabled and Native AOT
execution. Probability and transition constants are shared with the existing
interval writer; the interval coordinates use arbitrary-precision arithmetic.

Four complete minute decodes run in current/candidate/candidate/current order
under the preceding CPU, thread and GC settings. All 1,441 pictures, YUV bytes,
rational timestamps, durations, EOF and released-storage checks pass. CPU
medians are 20.228 and 22.466 seconds: the candidate is 11.06% slower in this
series. Elapsed medians are 21.905 and 24.548 seconds; peak RSS medians are
61,306 and 56,590 KiB.

The candidate is not adopted. No reverse-order, CAVLC, encoding or complete-HLS
extension is run. Fewer calls in the source do not establish lower complete
decoding cost; these observations do not identify the cause of the CPU change
or update the independent FFmpeg comparison.

## Uniform prediction reuse

The subsequent [uniform prediction comparison](h264-uniform-prediction.md)
checks the current decoder on complete three-minute input and refreshes the
one-thread external decoder comparison. It preserves decoded samples and
encoded bytes.

## Current complete native diagnostic

A subsequent PC sampling run checks the accepted uniform-prediction decoder
on the complete 60.041667-second High/CABAC input. All 1,441 pictures and
1,992,038,400 output bytes match the independent reference, including rational
timestamps, durations, EOF and released media storage. The run uses logical
CPU 2 and one media-processing thread.

The recording contains 16,911 resolved native samples, with the final sample
0.000356 seconds before process exit. The collector reports its timer-change
warning; the complete record and warning are retained. These are diagnostic
sample estimates, not exact unperturbed function CPU times or an acceleration
measurement.

| Function | Exclusive sampled seconds | Inclusive sampled seconds |
| --- | ---: | ---: |
| Boundary-strength fill | 0.389 | 2.766 |
| Direct prediction region | 0.201 | 3.931 |
| Motion lookup | 0.506 | 0.958 |
| Motion assignment | 0.654 | 0.752 |
| All memory moves | 1.150 | 1.150 |

Parent inclusive values contain their children and must not be added together.
The profile identifies repeated prediction and boundary calculations for
further investigation. It does not establish that removing one copy or helper
will reduce complete processing cost. The
[interior source-region control](h264-fractional-motion.md#interior-source-region-control)
preserves all output samples but does not produce a CPU improvement.

## Region-scoped spatial direct prediction control

Two repeated Native AOT diagnostic pairs decode the complete 60.041667-second
High/CABAC input without changing prediction decisions. Every output byte,
frame timestamp and duration, EOF and released-storage check passes. The
counts agree between repetitions and between both diagnostic versions:

| Work | Count |
| --- | ---: |
| Direct regions | 3,311,779 |
| Already fully uniform regions | 3,305,413 |
| Current prediction partitions | 3,330,877 |
| Conservative equal-motion rectangular cover | 3,320,471 |
| All motion lookups | 53,893,364 |
| Direct-region motion lookups | 46,647,258 |
| Repeated identical lookups within an unchanged region | 13,095,903 |
| Regions whose preferred spatial vectors make co-located lookup unnecessary | 1,959,380 |

The rectangular cover removes only 0.31% of partition calls and is not
implemented. The spatial invariant applies to 59.16% of these regions. This
input exercises spatial direct prediction with 8x8 inference; these counts
do not characterize temporal direct prediction or 4x4 inference workloads.

A separate calculation reads the two preferred spatial vectors once per
region and omits co-located reads only when the zero condition cannot change
either selected vector. The original neighbor selection and prediction
arithmetic are shared. Temporal mapping, weights, reference validation and
prediction partitions retain their existing semantics. This is consistent
with the separation of preferred spatial motion and co-located zero handling
in the [FFmpeg direct prediction implementation](https://github.com/FFmpeg/FFmpeg/blob/n6.1.1/libavcodec/h264_direct.c).

Independent controls pass 1,920 whole-region pixel/motion cases and 16,384
neighbor-availability, reference-selection and extreme-vector cases in
managed, hardware-disabled and strict Native AOT execution. The region
matrix also covers temporal prediction, 4x4 inference, long-term references
and explicit and implicit weights. All 725 H.264 tests pass with hardware
intrinsics enabled and disabled; the selection checks allocate zero managed
bytes after warmup.

Eight complete 180.125-second Native AOT decodes run serially on logical CPU 2,
with one media-processing thread, ordinary GC and default compiler settings.
Each writes the same 4,323 pictures and 5,976,115,200 bytes, including every
rational clock, EOF and released-storage check. Medians are:

| Order | Current CPU, s | Scoped CPU, s | CPU change | Current elapsed, s | Scoped elapsed, s | Current RSS, KiB | Scoped RSS, KiB |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Current, scoped, scoped, current | 51.777 | 48.759 | -5.83% | 66.679 | 65.192 | 72,392 | 73,196 |
| Scoped, current, current, scoped | 51.123 | 51.027 | -0.19% | 88.556 | 76.068 | 74,124 | 74,072 |

The reverse series does not reproduce the minimum 5% CPU reduction. The
calculation is not adopted, and CAVLC, encoding and full-HLS extensions are
not run. The independent FFmpeg comparison remains unchanged.

Two additional complete native PC recordings contain 51,289 and 51,000
resolved samples, ending 0.000464 and 0.011193 seconds before exit. The
collector timer-change warnings are retained. Motion-lookup inclusive
sample estimates decrease from 3.035 to 1.940 seconds, while total sampled
CPU remains 51.289 and 51.000 seconds. GC allocation includes approximately
0.655 and 0.619 sampled seconds; collection includes 0.097 and 0.062 seconds.
These diagnostics support less repeated lookup work but do not establish
GC as the cause of ordinary-run variation or prove sustained acceleration.
Inclusive parent and child estimates overlap and must not be added together.
