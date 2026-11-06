Complete comparisons of per-orientation and paired macroblock boundary
calculations are recorded in [boundary classification](h264-boundary-controls.md).
Complete fractional-filter coverage counts and cache capacity estimates are
recorded in [interpolation reuse](h264-interpolation-reuse.md).

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
The Native AOT consumer builds with warnings treated as errors.

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
and 63,020 KiB. The variant is not adopted; the remaining CAVLC and reverse-order
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
hardware-disabled and Native AOT execution.

Four complete CABAC decodes preserve every raw sample, timestamp and duration,
EOF and zero owned media. CPU median changes from 20.870151 to 20.426722 seconds,
a 2.13% reduction. The shortcut is not adopted; reverse-order and CAVLC
comparisons were not run.
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

Boundary strength resolves physical picture identities before comparing
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

All 16 complete outputs remain exact. CABAC CPU decreases by 7.82% and 5.49%
in the two execution orders; neither CAVLC series regresses. The overall CABAC
CPU medians are 21.895 and 20.182 seconds. CAVLC overall medians differ by only
0.78%, so these results do not establish a sustained CAVLC speedup. CABAC RSS
medians are 61,824 and 65,384 KiB; allocation traffic is essentially unchanged.

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
also pass. The matrix runs in managed, hardware-disabled and Native AOT
execution, together with existing bidirectional and multi-slice checks.

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
pass in managed, hardware-disabled and Native AOT execution.

The candidate is not adopted.
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
managed, hardware-disabled and Native AOT execution. The region
matrix also covers temporal prediction, 4x4 inference, long-term references
and explicit and implicit weights. The selection checks allocate zero managed
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

## Sustained external decoder comparison

A subsequent serial comparison decodes 180.125 seconds: the independently
checked 60.041667-second High/CABAC scene repeated three times with continuous
timestamps. This is a sustained operation, rather than three minutes of
distinct scenes. The decoder code is unchanged during this comparison.

Both programs run on logical CPU 2 with one decoder thread. FFmpeg filters
also use one thread. The raw-output encoder selects its automatic count from
the available CPU affinity. This constrains media computation; runtime and
I/O thread counts need not match. Native AOT uses the existing default compiler
settings and ordinary GC.

The order is FFmpeg, Mediana, Mediana, FFmpeg. Each operation writes exactly
4,323 pictures and 5,976,115,200 planar YUV bytes with the same SHA-256.
Mediana also verifies every rational timestamp, duration, EOF and released
media storage. Medians are:

| Measurement | Mediana | FFmpeg 6.1.1 |
| --- | ---: | ---: |
| Process CPU, s | 51.354 | 11.055 |
| Elapsed, s | 93.457 | 71.070 |
| Peak RSS, MiB | 76.844 | 76.734 |

The CPU ratio is **4.645 times**. The previous 60.041667-second comparison
reported 5.056 times; no decoder change separates the two measurements, so
their difference is not evidence of an optimization. FFmpeg elapsed time
varies from 42.725 to 99.415 seconds within the new series. CPU measurements
and all individual observations are retained; elapsed medians on this host
do not establish a stable throughput ratio.

### Matched native function diagnostics

Two additional complete operations produce the same output bytes. Matching
official Ubuntu ELF debug files are checked against the installed FFmpeg
and library build IDs. The programs and libraries used for comparison remain
byte-identical to the installed files.

The recordings contain 9,435 FFmpeg samples and 52,170 Mediana samples at the
requested one-millisecond CPU sampling interval. Their final samples precede
exit by 0.280634 and 0.022303 seconds. Both collector timer-change warnings
are retained. These are diagnostic estimates, rather than exact unperturbed
function CPU times.

| Mediana function group | Exclusive sampled seconds |
| --- | ---: |
| Deblocking classes | 11.463 |
| CABAC classes | 7.575 |
| Luma and chroma interpolation | 3.443 |
| Memory moves | 3.680 |

Boundary-strength filling includes 8.536 sampled seconds, while the complete
deblocking call includes 12.623. Those inclusive values overlap with the
exclusive groups and must not be added to them. GC allocation includes 0.568
sampled seconds and collection 0.085; this recording does not support GC
as the dominant explanation of the total gap.

FFmpeg's sized ELF functions resolve normally, including CABAC and
`loop_filter`. Some assembly entries have no ELF size and appear as broad
address-only groups in the initial function report. Independently reading
the original sampled PCs resolves 4,997 library samples inside explicitly
sized functions. Another 2,818 samples lie between zero-sized function entries;
all sampled addresses are checked against disassembled instruction boundaries.
Two library samples remain unresolved. Shared unnamed assembly helpers retain
their ambiguity and are not attributed solely from the preceding symbol name.

The boundary-strength assembly entry contains 338 samples at 112 distinct
instruction addresses. Its source calculates several strengths together from
packed reference, motion and residual data. Mediana's current profile instead
shows substantial work in individual strength, residual and physical-reference
checks. This is a concrete direction for a grouped calculation with preserved
validation semantics, not proof that one replacement removes the whole gap.
The comparison uses the
[FFmpeg strength calculation](https://github.com/FFmpeg/FFmpeg/blob/n6.1.1/libavcodec/x86/h264_deblock.asm)
and [DSP selection](https://github.com/FFmpeg/FFmpeg/blob/n6.1.1/libavcodec/x86/h264dsp_init.c).

## Four-segment packed boundary calculation control

A separate control computes mode, residual and motion comparisons for four
segments together. It retains physical picture identity, duplicate reference
indices, L0/L1 exchanges, uniform-prediction validation and sequential error
handling. Motion components retain their full Int32 range: packed subtraction
checks signed overflow before accepting a difference from -3 through 3.
There is no new media buffer, persistent cache, worker or compiler ISA override.

Independent widened scalar arithmetic agrees on 7,079,332 motion lanes in
managed, hardware-disabled and Native AOT execution. The same modes
pass 264,192 topology patterns, physical-reference and mixed-edge controls,
31,248 uniform/mutation cases and 1,920 range/partial-write cases. Disassembly
confirms packed motion instructions in the actual Native AOT consumer.

Eight complete 180.125-second operations produce the same 4,323 pictures and
5,976,115,200 output bytes, every rational timestamp and duration, EOF and
released media storage. Medians are:

| Order | Current CPU, s | Packed CPU, s | CPU change | Current elapsed, s | Packed elapsed, s | Current RSS, KiB | Packed RSS, KiB |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Current, packed, packed, current | 48.432 | 45.794 | -5.45% | 71.368 | 63.786 | 71,970 | 82,944 |
| Packed, current, current, packed | 47.449 | 49.680 | +4.70% | 74.611 | 70.871 | 71,912 | 75,210 |

The reverse order does not reproduce the required 5% CPU reduction, and peak
RSS increases. The control is not adopted. CAVLC and encoding extensions are
not run, and the external decoder comparison remains unchanged.

One further complete diagnostic operation matches every output check. Its
45,646 PC samples end 0.000239 seconds before exit; the collector timer-change
warning is retained. Deblocking includes 9.431 sampled seconds, grouped
strength filling 5.731, and packed motion arithmetic 0.395. These overlapping
sample estimates help inspect the calculation but do not override the failed
ordinary-run control or establish sustained acceleration. GC allocation
includes 0.580 sampled seconds and collection 0.073.

## Complete-operation repeatability and motion lookup control

Eight complete decodes execute the same accepted Native AOT binary under
labels A and B, in ABBA and BAAB order. The 180.125-second input repeats the
same minute scene three times. Every operation produces the same 4,323
pictures and 5,976,115,200 raw bytes, with exact rational timestamps,
durations, EOF and released media storage.

The sham CPU differences between label medians are +1.17% and -1.75%.
Individual CPU times range from 46.434 to 49.139 seconds; their sample
coefficient of variation is 2.03%. These observations describe this batch;
they establish neither future measurement stability nor the cause of
variation in earlier batches.

A subsequent control borrows motion indices, vectors and modes once per
neighbor group. It preserves the prediction algorithm, full Int32 vector
values and current slice availability. Independent checks cover 525,640
mode/reference/vector/coordinate/slice cases, invalid and unavailable lists,
disposed owners, metadata mutation, allocation and release. Those checks
and 384 direct-region pixel/motion cases pass with original callers and
with the control, including managed, hardware-disabled and Native
AOT execution.

Four complete decodes run in current/control/control/current order:

| Metric | Current | Borrowed lookup |
| --- | ---: | ---: |
| Median CPU, seconds | 47.309 | 46.224 |
| Median elapsed, seconds | 63.916 | 66.476 |
| Median peak RSS, KiB | 73,162 | 73,966 |

Every output byte and clock check passes. The observed CPU reduction is
2.29%, below the predefined 5% requirement, so the control is not adopted.
Reverse-order, CAVLC and encoding extensions are not run. The accepted
decoder and its last external CPU comparison remain unchanged.

The motion-lookup checks pass in managed execution, with hardware intrinsics disabled
and in a Native AOT consumer.

## Macroblock motion and coded residual region controls

Two further controls use the same complete 180.125-second High/CABAC input:
4,323 pictures from a minute scene repeated three times with continuous
timestamps. Each first series runs before/after/after/before, one decoder
thread on one processor, with unchanged compilation settings.

The motion control borrows the existing mutable metadata once per list and
macroblock, carrying it through prediction and immediate partition writes.
It preserves full Int32 vectors, live slice boundaries and reference-index
visibility. Independent read/write and direct-prediction checks pass with
hardware intrinsics enabled and disabled, and under Native AOT.

The residual control reconstructs each absent coded 8x8 luma region once
instead of visiting its four 4x4 blocks separately. Present regions retain
the original entropy order. All 22,464 independent cases pass.

| Control | CPU before, seconds | CPU after, seconds | CPU change | Peak RSS before/after, KiB |
| --- | ---: | ---: | ---: | ---: |
| Macroblock motion access | 47.034 | 45.618 | -3.01% | 74,688 / 73,792 |
| Coded residual regions | 47.671 | 47.029 | -1.35% | 73,324 / 72,400 |

These are medians within each separate series. Median elapsed changes from
53.972 to 54.411 seconds and from 63.275 to 69.850 seconds, respectively.
All four outputs in each series have identical 5,976,115,200 decoded bytes,
rational timestamps, durations, EOF and released media storage. Neither
control reaches the predefined 5% CPU reduction, so neither is adopted.
Reverse-order, CAVLC and encoding extensions are not run. These controls
provide no new comparison with FFmpeg or complete HLS processing.

The retained [residual checks](../tests/Shared/H264ResidualRegionTestInput.cs)
cover all 16 luma masks, 52 quantizers, nine macroblock positions and three
zero/DC/mixed coefficient patterns. They verify pixels, input consumption,
completed neighbor metadata, unchanged prediction and adjacent storage,
and media release. They pass in managed execution, with hardware intrinsics disabled and in a Native
AOT consumer.

## Combined CABAC decision table control

A control combines the least-probable interval, both next contexts and the
current symbol in one indexed decision table. It preserves the checked bit
reader and skips normalization only when the selected range is already
normalized. Bypass, termination and input consumption remain unchanged.

Independent checks use the probability and transition tables from
[H.264, June 2026](https://www.itu.int/rec/T-REC-H.264-202606-I),
Tables 9-44 and 9-45. They cover 245,504 cases: all 64 probability states,
both symbols and every valid initial offset at ranges 256, 320, 384, 448
and 510. They verify the decoded symbol, next context, arithmetic interval,
bit position and alignment, including every normalization shift from zero
through seven. The original and modified decoders pass these checks in
managed, hardware-disabled and Native AOT execution.

Four complete 180.125-second decodes run in before/after/after/before order:

| Metric | Current | Combined table |
| --- | ---: | ---: |
| Median CPU, seconds | 48.469 | 48.283 |
| Median elapsed, seconds | 77.312 | 83.677 |
| Median peak RSS, KiB | 73,634 | 74,334 |

Each produces the same 4,323 pictures and 5,976,115,200 raw bytes, rational
timestamps, durations, EOF and released media storage. The observed CPU
reduction is 0.38%, below the predefined 5% requirement, so the control is
not adopted. Reverse-order, CAVLC and encoding extensions are not run;
the external decoder and complete HLS comparisons remain unchanged.

The [normative decision checks](../tests/Shared/H264CabacDecisionTestInput.cs)
run in managed execution, with hardware intrinsics disabled and in a Native
AOT consumer.

## Checked-byte arithmetic register controls

Two further controls retain the already validated suffix of the current
input byte in the arithmetic register. One uses a variable scale and an
explicit buffered-bit count; the other uses a fixed scale and a low-bit
boundary marker. Both remove the bit-at-a-time normalization loop and read
the next byte only when its first bit is required. They preserve the
logical reader position after every bin and the partial range, offset and
reader state when checked input rejects a damaged byte.

Both pass the 245,504 normative decision cases, 32,768 exact interval
decisions, 81,920 false terminations, all normalization shifts and byte
phases, malformed-input and zero-allocation checks in managed,
hardware-disabled and Native AOT execution.

Each control runs four complete 180.125-second operations in
before/after/after/before order. Medians within each separate series are:

| Register | CPU before/after, seconds | CPU change | Elapsed before/after, seconds | Peak RSS before/after, KiB |
| --- | ---: | ---: | ---: | ---: |
| Variable scale | 48.045 / 47.095 | -1.98% | 70.254 / 59.704 | 74,698 / 74,012 |
| Fixed scale and marker | 47.791 / 47.538 | -0.53% | 68.220 / 65.723 | 71,176 / 72,944 |

Every operation produces the same 4,323 pictures and 5,976,115,200 raw
bytes, rational timestamps, durations, EOF and released media storage.
Neither control reaches the predefined 5% CPU reduction, so neither is
adopted. Reverse-order, CAVLC and encoding extensions are not run. The
decoder and its external and complete HLS comparisons remain
unchanged.

## Macroblock intra and residual classification control

A separate control classifies all sixteen luma cells of a macroblock
into intra and residual bit masks once per edge orientation. An 8x8
transform expands residual presence across its four 4x4 cells. Internal
edges reuse those masks; physical-reference and motion comparisons retain
the existing rules. The masks use local values and add no media allocation.

All 524,288 exhaustive intra/residual combinations agree with scalar
boundary strengths in managed, hardware-disabled and Native AOT
execution. Existing reference identity, mutation, error priority, partial
write and zero-allocation checks pass.

Four complete 180.125-second Native AOT decodes run in current, candidate,
candidate, current order, pinned to one CPU. Every run matches all 4,323
pictures, 5,976,115,200 output bytes, rational timestamps and durations,
EOF and released media storage. Medians are:

| Measurement | Current | Candidate |
| --- | ---: | ---: |
| CPU, seconds | 52.154649 | 53.401616 |
| Elapsed, seconds | 122.813818 | 104.269049 |
| Peak RSS, KiB | 75078 | 71696 |

The CPU change is +2.391%. The complete series does not meet the
required 5% CPU reduction, so the control is not adopted. Reverse-order,
CAVLC and encoding extensions are not run. This control establishes no new
FFmpeg or complete HLS performance ratio.
