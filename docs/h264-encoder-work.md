Complete raw-video encoding at `452d803` was profiled to distinguish coefficient
writing from inverse transforms and reconstructed-pixel writes. The diagnostic
consumer processes the same 1,441-picture input and settings as the
[stage comparison](h264-stage-performance.md#input-and-measurement).

## Diagnostic work distribution

The Native AOT diagnostic runs on one assigned CPU. Every output byte matches the
independently decoded MPEG-TS reference, with EOF and zero retained media storage.

| Timed region | Calls | Instrumented elapsed seconds |
| --- | ---: | ---: |
| Picture encoding | 1,441 | 53.301463 |
| Inter-picture encoding | 1,430 | 51.944159 |
| Intra-picture encoding | 11 | 1.159524 |
| Inter luma writing | 5,148,000 | 17.172375 |
| Chroma writing | 5,187,600 | 5.377941 |
| CAVLC residual writing | 67,204,034 | 8.396480 |
| Inverse 4x4 transform | 43,076,450 | 3.543609 |
| Reconstructed block writes | 43,076,450 | 2.307647 |
| Prediction block copies | 43,076,450 | 0.939168 |

These nested elapsed measurements include timer overhead and effects on
inlining. They are not CPU percentages and must not be summed. The complete
diagnostic process uses 54.139285 CPU seconds, 54.457094 elapsed seconds and
52.77 MiB peak RSS. Uninstrumented measurements determine whether a change is
useful.

## Direct CAVLC level experiment

The prefix and suffix rules in [ITU-T H.264](https://www.itu.int/rec/T-REC-H.264-202606-I/en),
clause 9.2.2, permit direct derivation of a complete level codeword. A diagnostic
variant replaces prefix enumeration and three bit-writer calls with a direct
calculation and one write. A second variant removes its intermediate writer
method and inlines codeword arithmetic; Native AOT disassembly confirms the
removed calls.

Every possible 16-bit level-code value was checked against independent prefix
interval enumeration at suffix lengths zero through six. Every representable
word was also written and parsed at all eight bit alignments. All complete
encoded transports match the original 78,332,832-byte reference exactly.

The binaries run serially on logical CPU 2 with one processing thread and no
overlapping build, test or media workload. Each observation encodes the complete
60.041667 seconds and reaches EOF. These are shared-host measurements.

| Variant and order | Before CPU median, s | Variant CPU median, s | CPU reduction |
| --- | ---: | ---: | ---: |
| Direct word, before/after/after/before | 44.257118 | 42.764489 | 3.37% |
| Direct word, after/before/before/after | 41.476862 | 41.470086 | 0.02% |
| Inlined direct word, before/after/after/before | 41.583689 | 40.964053 | 1.49% |

The first individual pair appeared 11.15% faster, but the reversed series did
not confirm that gain. The final variant's small observed reduction does not
justify an added codeword abstraction and inlining requirement for the current
performance work. Neither variant was adopted at that baseline.

A subsequent [direct writer implementation](h264-cavlc-encoding.md) uses no
separate codeword type or explicit inlining attribute. Its current Speed
comparison establishes a local CPU reduction with identical encoded output;
the preceding observations remain specific to their original variants.

## Complete reference-plane experiment

A diagnostic variant prepares padded integer samples and the horizontal,
vertical and diagonal half-sample planes once per reference picture. One
budgeted workspace is reused between pictures and rate-control retries. Sample
views are read-only; completion, reset, failure and disposal release storage.
The interpolation arithmetic follows clause 8.4.2.2.1 of H.264. The
[x264 frame filter](https://github.com/mirror/x264/blob/master/common/mc.c)
provides a reference for organizing reusable planes.

Independent scalar convolution checks cover all 49 refinement offsets, ringing
patterns, borders and extreme coordinates. Additional tests check unchanged
published pixels, allocation-free reuse, memory-budget rejection and
cancellation. All 25 focused managed checks pass.

The same complete raw input, encoder settings, CPU assignment and serial
measurement procedure are used as above:

| Execution order | CPU seconds | Elapsed seconds | Peak RSS, KiB |
| --- | ---: | ---: | ---: |
| before-1 | 41.406728 | 43.529593 | 51,748 |
| variant-1 | 50.727368 | 53.659924 | 70,656 |
| variant-2 | 78.630861 | 81.186218 | 69,108 |
| before-2 | 53.591345 | 55.226649 | 50,904 |

The CPU medians are **47.499037 and 64.679115 seconds**; the variant is 36.17%
slower in this batch. RSS medians are 50.12 and 68.24 MiB. The substantial
variation within each version prevents a portable throughput claim or a causal
explanation from this series alone. No benefit is established, and the variant
was not adopted.

All four complete 78,332,832-byte transports match the independently decoded reference
exactly. Every observation produces 1,441 packets, reaches EOF and releases media
storage.

A separate full-input census records **5,057,417 prepared neighborhoods** over
5,148,000 inter-picture macroblocks, about 98.24%. Thus rare fractional refinement does
not explain the result on this input. The neighborhood filters calculate 5,370,976,854
scalar-equivalent six-tap output positions. Preparing complete planes for all 1,430
inter pictures calculates at most 4,001,855,000 such positions. These counts are not
instruction counts or CPU costs; fewer positions alone did not establish a useful
implementation. The census also preserves the complete reference transport and releases
all owned media.

The existing neighborhood implementation remains in use. Fractional candidate
evaluation, memory access and generated Native AOT instructions require further
measurement before another preparation strategy is selected. The established
[integer-search result](h264-integer-search.md) and
[complete-film HLS result](hls-hd-encoding.md) remain the applicable product
measurements; this experiment establishes no improvement over either.

## Fractional-filter instruction experiments

Three further variants retain the same search order, thresholds, interpolation
arithmetic and output bytes. Each comparison uses Native AOT
binaries and eight complete serial observations in before/after/after/before,
then after/before/before/after order. CPU assignment, input and processing-thread
settings match the preceding experiments.

| Variant | Before CPU median, s | Variant CPU median, s | CPU change |
| --- | ---: | ---: | ---: |
| Advance candidate row addresses directly | 39.666729 | 39.548312 | -0.30% |
| SSSE3 paired six-tap products | 41.387066 | 42.005126 | +1.49% |
| SSE2 saturating pixel packing | 39.786442 | 40.060450 | +0.69% |

Native disassembly confirms that direct row addressing removes two integer
multiplications from the averaging loop. The SSSE3 variant uses three paired
multiply-add operations for the six filter coefficients. Saturating packing
removes a separate half-sample helper call and reduces the diagonal helper from
240 to 192 bytes. These instruction changes do not establish an encoding CPU
benefit. The first packing observation appeared faster, but the complete
reversed series did not confirm that result.

All 24 complete encoded transports match the same 78,332,832-byte reference.
Each contains 1,441 pictures, reaches EOF and releases owned media storage.
Focused managed checks and Native AOT builds pass for every variant.
Independent scalar checks cover packing rounding, clipping, overflow boundaries
and unchanged input storage; software-only fractional checks also pass for the
SSSE3 and packing variants.

The packing variant also completes eight independent full decoding observations.
Every result matches all 1,992,038,400 reference pixel bytes. Median CPU changes
from 18.735485 to 18.149898 seconds (-3.13%); median elapsed time changes from
19.208179 to 19.339005 seconds. This small shared-host observation, without an
encoding benefit, does not justify adopting the additional processor-specific
path for the current performance work. None of the three variants was adopted.
Product sources and reference fixtures remain identical to the accepted
implementation.


## Current coefficient and bit-writing controls

A complete diagnostic at `5f9e7d3` includes the accepted
[reconstruction storage reuse](h264-reconstruction-storage.md). It separates
motion refinement, coefficient analysis and coefficient writing without an
additional skip-candidate probe. The input and settings remain those of the
complete stage comparison, with all 1,441 pictures and 60.041667 seconds.

| Timed region | Calls | Instrumented elapsed seconds |
| --- | ---: | ---: |
| Picture encoding | 1,441 | 45.892629 |
| Motion search | 5,148,000 | 19.011727 |
| Fractional refinement | 5,148,000 | 13.701640 |
| Neighborhood preparation | 5,057,417 | 5.043650 |
| Inter luma analysis | 5,021,657 | 3.091849 |
| Chroma analysis | 5,061,257 | 1.513612 |
| CAVLC residual writing | 67,204,034 | 7.419697 |
| Inverse 4x4 residual transform | 43,076,450 | 3.149033 |

The complete diagnostic uses 46.592770 CPU seconds. Timers include overhead,
regions overlap, and instrumentation can affect inlining. These are not CPU
percentages. All output bytes match the independent transport reference, and
media storage is released at EOF.

Two variants target coefficient writing. Word packing assembles a complete
short codeword with the pending bits before emitting its complete bytes; wider
writes are split into two words. Nonzero collection uses four-lane integer
comparisons to collect occupied coefficient positions, then visits only those
positions in the original descending order. The unchanged encoder is the
baseline for each variant; the variants are measured separately.

Each comparison uses Native AOT binaries, sequential execution on logical
CPU 2, one processing thread and `DOTNET_PROCESSOR_COUNT=1`. Each run encodes the
complete input.

| Execution order | Word-packing CPU, s | Nonzero-collection CPU, s |
| --- | ---: | ---: |
| before-1 | 44.205802 | 42.956439 |
| after-1 | 44.653400 | 40.649346 |
| after-2 | 41.554626 | 43.347350 |
| before-2 | 42.058169 | 40.768169 |
| after-3 | 41.219578 | 40.498269 |
| before-3 | 46.166762 | 43.534210 |
| before-4 | 43.023413 | 40.938410 |
| after-4 | 41.476831 | 41.088882 |

| Variant | Before CPU median, s | Variant CPU median, s | First-series CPU change | Reversed-series CPU change | Before / variant RSS median, MiB |
| --- | ---: | ---: | ---: | ---: | ---: |
| Word packing | 43.614608 | 41.515729 | -0.06% | -7.28% | 37.55 / 40.34 |
| Nonzero collection | 41.947425 | 40.869114 | +0.32% | -3.42% | 35.90 / 37.81 |

The apparent aggregate reductions do not repeat across execution orders. Neither
variant establishes a stable CPU improvement, and neither is adopted. Neither
establishes lower RSS or cumulative allocation. The existing bit writer,
coefficient traversal, public contracts and encoded output remain unchanged.

All 16 complete transports are exactly the independently decoded
78,332,832-byte reference, SHA256
`d3090ee699cfdcc4330ec9815adaa1e7b08de1d95530e2e7af2e1596a399e543`.
Every run produces 1,441 packets, reaches EOF and releases owned media storage.

## Motion-search policy controls

Two further policies change coding decisions. Both use the same complete
1,441-picture raw input, quantizer, rate settings, GOP, Native AOT
consumers and single-CPU execution as the preceding controls. Each policy has
one complete baseline/variant pair, followed by independent decoding of every
picture and PSNR measurement against the raw source. These pilot pairs do not
establish repeatable throughput improvements.

The first policy ranks candidates by luma SAD plus a quantizer-dependent cost
for signed motion-vector differences. Chroma prediction is formed only for the
selected vector. Half- and quarter-sample refinement each check four cardinal
neighbors. This differs from adding vector cost to the existing combined YUV
objective alone.

The second policy retains the combined YUV SAD objective and its original tie
rule. It adds rounded and exact motion from the corresponding block in the
previous reference picture, tests exact spatial and temporal predictors before
refinement, and uses four cardinal neighbors at each fractional step. Motion
from an intra block or a different reference index is not used as a temporal
candidate.

| Policy | Baseline / variant CPU, s | Baseline / variant transport bytes | Baseline / variant aggregate PSNR, dB |
| --- | ---: | ---: | ---: |
| Luma and vector cost, cardinal refinement | 46.500992 / 37.271854 | 78,332,832 / 78,337,344 | 45.093622 / 44.624592 |
| Temporal candidates, combined YUV error, cardinal refinement | 47.170759 / 44.898634 | 78,332,832 / 78,319,108 | 45.093622 / 44.944711 |

The first policy loses 0.469030 dB while producing a slightly larger transport.
The second loses 0.148911 dB for a 0.01752% transport reduction. Neither policy
establishes a useful rate/quality/resource improvement, and neither is adopted.
Their one-pair CPU differences cannot be presented as equal-quality speedups.

All four transports contain 1,441 pictures with exact PTS/DTS and the expected
144-picture IDR spacing. Each encoding reaches EOF and releases owned media
storage. Native AOT compilation passes. The second policy's extended
fractional neighborhood also passes 5,400 independent direct-convolution cases,
including border and extreme-coordinate cases, source/output guards and
allocation checks. These checks establish prediction correctness, not improved
compression decisions. The production search policy and output remain unchanged.

## Bounded byte-residual pipeline control

A diagnostic combines byte subtraction, the forward 4x4 transform and
quantization without storing and reloading an intermediate coefficient block.
Quantization parameters are prepared once for a macroblock's luma or chroma
analysis. The generic integer quantizer remains unchanged.

Byte residuals are bounded by 255 in magnitude. The largest absolute row sum
of the transform matrix is six, so the transformed coefficient magnitude is at
most `255 * 6 * 6 = 9180`. The largest quantization factor and rounding bias
keep the multiplication below the signed 32-bit limit. The diagnostic uses
four-lane [SSE4.1 multiplication](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.intrinsics.x86.sse41.multiplylow?view=net-10.0)
on supported processors and the existing transform and quantizer otherwise.
Native disassembly confirms four `pmulld` operations in the combined path;
the existing standalone SSE2 quantizer uses eight `pmuludq` operations.
These methods perform different amounts of work, so their code sizes are not a
throughput comparison.

An independent direct-matrix and scalar 64-bit quantization oracle checks
276,480 input blocks, all 52 quantizers, both coefficient scans, chroma's
unquantized DC, padded strides, output guards and unchanged input samples.
The checks include every binary residual corner pattern at three quantizers.
They pass in Native AOT and with hardware intrinsics disabled. Repeated
prepared-block processing allocates no heap storage.

Eight complete Native AOT encodes use the same input, settings and serial single-CPU
measurement procedure as the preceding controls. Every observation produces the exact
78,332,832-byte reference transport, all 1,441 pictures, EOF and zero retained media
storage.

| Execution order | Before CPU median, s | Variant CPU median, s | CPU change |
| --- | ---: | ---: | ---: |
| before/after/after/before | 43.237429 | 40.537058 | -6.25% |
| after/before/before/after | 41.340740 | 40.699855 | -1.55% |
| All eight observations | 42.179677 | 40.537058 | -3.89% |

The observed reduction is small and varies substantially between orders.
Median peak RSS is 36.73 versus 37.95 MiB; cumulative managed allocation is
2,283,391,280 versus 2,283,383,620 bytes. This control establishes neither a
large CPU improvement nor a memory improvement. The added processor-specific
path is not adopted. The production coefficient pipeline and output remain
unchanged.

## Inverse-transform and pixel-addition controls

Two separate diagnostics test reconstruction arithmetic without changing motion
search, quantization, coding decisions or output. Both use the complete
1,441-picture, 1,280x720 input, Native AOT executables and serial execution
on one CPU with one reported .NET processor. CPU time includes user and system
time. Each table row contains two baseline and two variant observations.

The first diagnostic uses four 32-bit SIMD lanes for the 4x4 inverse transform.
It follows the row-before-column transform and final rounding in
[H.264 section 8.5.12](https://www.itu.int/rec/T-REC-H.264-202606-I/en).
An explicit coefficient bound keeps both transform passes within signed 32-bit
arithmetic; inputs outside that bound retain the existing checked wide path.
The DC-only shortcut remains unchanged. Arithmetic checks cover all 52
quantizers, separate DC, overlapping input/output spans, overflow behavior and
destination guards. The final Native AOT diagnostic passes 438,256 cases,
including values immediately around the SIMD eligibility thresholds.

| Operation and execution order | Baseline CPU median, s | Variant CPU median, s | CPU change |
| --- | ---: | ---: | ---: |
| Decode, baseline/variant/variant/baseline | 19.495900 | 19.193617 | -1.55% |
| Decode, variant/baseline/baseline/variant | 21.521384 | 21.035520 | -2.26% |
| Encode, baseline/variant/variant/baseline | 39.357833 | 36.774263 | -6.56% |
| Encode, variant/baseline/baseline/variant | 36.478276 | 36.507799 | +0.08% |

The small decoding difference does not justify the extra specialized path.
The encoding reduction is not reproduced in the reverse order. This variant
is not adopted.

The second diagnostic changes only prediction-plus-residual addition and
clipping. It uses signed saturating packing and addition, followed by unsigned
byte packing. This preserves the original clipping result for every signed
32-bit residual, including the extreme values. It does not include the first
diagnostic's inverse transform. Its 98,304 arithmetic checks pass in managed
execution, Native AOT and managed execution with hardware intrinsics
disabled. They cover 4x4 and 8x8 blocks, padded strides, unaligned offsets,
pixel ordering and destination guards. Repeated arithmetic allocates no heap
storage.

| Operation, baseline/variant/variant/baseline | Baseline CPU median, s | Variant CPU median, s | CPU change |
| --- | ---: | ---: | ---: |
| Decode | 21.082288 | 21.984219 | +4.28% |
| Encode | 43.799105 | 45.905218 | +4.81% |

This control provides no evidence of acceleration and is not adopted. A reverse
series is not performed for this variant.

All 24 complete operations in these final comparisons reach EOF, process all
1,441 pictures and release owned media storage. Every decoded output matches
the independent 1,992,038,400-byte raw reference; every encoded output matches
the independent 78,332,832-byte transport reference. Neither diagnostic changes
the production reconstruction pipeline or establishes a memory improvement,
new FFmpeg ratio or complete-film HLS improvement.
