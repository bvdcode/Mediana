Complete raw-video encoding at `452d803` was profiled to distinguish coefficient
writing from inverse transforms and reconstructed-pixel writes. The diagnostic
consumer processes the same 1,441-picture input and settings as the
[stage comparison](h264-stage-performance.md#input-and-measurement).

## Diagnostic work distribution

The strict Native AOT diagnostic runs on one assigned CPU. Every output byte
matches the independently decoded MPEG-TS reference, with EOF and zero retained
media storage. Source hashes remain unchanged.

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

Frozen binaries run serially on logical CPU 2 with one processing thread and no
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
performance work. Neither variant was adopted. The existing CAVLC implementation
and its validated encoding output remain unchanged.

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
cancellation. All 25 focused managed tests and the strict Native AOT build pass.

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

All four complete 78,332,832-byte transports match the independently decoded
reference exactly. Every observation produces 1,441 packets, reaches EOF and
releases media storage. Source, input and binary hashes remain unchanged during
the comparison.

A separate full-input census records **5,057,417 prepared neighborhoods** over
5,148,000 inter-picture macroblocks, about 98.24%. Thus rare fractional
refinement does not explain the result on this input. The neighborhood filters
calculate 5,370,976,854 scalar-equivalent six-tap output positions. Preparing
complete planes for all 1,430 inter pictures calculates at most 4,001,855,000
such positions. These counts are not instruction counts or CPU costs; fewer
positions alone did not establish a useful implementation. The census also
preserves the complete reference transport and releases all owned media.

The existing neighborhood implementation remains in use. Fractional candidate
evaluation, memory access and generated Native AOT instructions require further
measurement before another preparation strategy is selected. The established
[integer-search result](h264-integer-search.md) and
[complete-film HLS result](hls-hd-encoding.md) remain the applicable product
measurements; this experiment establishes no improvement over either.

## Fractional-filter instruction experiments

Three further variants retain the same search order, thresholds, interpolation
arithmetic and output bytes. Each comparison uses frozen strict Native AOT
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
Focused managed checks and strict Native AOT builds pass for every variant.
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
