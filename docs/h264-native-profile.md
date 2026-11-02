Complete H.264 encoding and decoding were sampled using an unchanged Native AOT
consumer with `OptimizationPreference=Speed`. A separate chroma-comparison
variant was then measured over complete encodes. It was not adopted: its CPU
reduction was 0.86%.

## Native stack sampling

The consumer uses the codec sources at `634ce0b`, SDK 10.0.402 and runtime
10.0.13. The input is the complete 1,441-picture, 1280x720, 24 fps sequence
described in the [stage comparison](h264-stage-performance.md). Encoding reads
the independently decoded YUV input; decoding reads the original MOV.

GDB samples native stacks without inserting timers into codec methods or
changing their compiled instructions. Each observation runs serially on logical
CPU 2 with `DOTNET_PROCESSOR_COUNT=1`. Sampling intervals vary between 80 and
140 milliseconds. Each stop captures up to 24 frames for every thread. The
thread with the greatest increase in user-plus-system CPU ticks since the
preceding sample contributes one selected stack. A sample without an active
thread contributes no selected stack.

Each stage runs in ordinary/sampled/sampled/ordinary order. CPU is complete
process user-plus-system time; elapsed time includes debugger stops.

| Stage | Ordinary CPU median, s | Sampled CPU median, s | Ordinary elapsed median, s | Sampled elapsed median, s |
| --- | ---: | ---: | ---: | ---: |
| Encode | 38.633067 | 38.961878 | 39.232253 | 51.746812 |
| Decode | 18.012574 | 17.664640 | 18.523142 | 23.368618 |

The sampled encoding CPU median is 0.85% higher. Decoding's small difference in
the opposite direction does not demonstrate negative measurement overhead.
Elapsed time increases substantially. These observations do not indicate a
large CPU perturbation, but scheduling, native signal handling, bounded stacks
and the shared host can bias the selected locations. Selected-stack counts are
not CPU-time percentages.

There are 586 encoder and 254 decoder stops, contributing 584 and 252 selected
stacks. Enclosing product functions give the following exclusive categories;
unmatched locations remain in the table.

| Enclosing work | Encode selected stacks | Decode selected stacks |
| --- | ---: | ---: |
| Motion search, including prediction and candidate scoring | 241 | 0 |
| CAVLC coefficient writing | 81 | 0 |
| Forward transform and quantization | 34 | 0 |
| Inverse transforms | 36 | 24 |
| Reconstructed-block writes | 19 | 9 |
| Deblocking | 0 | 36 |
| CAVLC coefficient reading | 0 | 27 |
| VLC reading outside the preceding category | 0 | 1 |
| Inter prediction and syntax | 0 | 31 |
| Other prediction | 0 | 3 |
| GC frames | 3 | 4 |
| Other and unattributed work | 170 | 117 |

The small number of GC-tagged points does not measure total GC CPU.
Reconstructed-block clipping is present, but this diagnostic does not support
selecting it as the principal encoder target. Motion-search stacks include
chroma interpolation, luma neighborhood preparation and candidate evaluation.
The counts do not establish their individual contributions to the FFmpeg gap.

Every ordinary and sampled operation completes all 1,441 pictures, reaches EOF
and releases all owned media storage. All four decoded outputs match every
byte of the independent 1,992,038,400-byte YUV reference. All four encoded
outputs match the independently decoded 78,332,832-byte MPEG-TS reference.
Product sources, fixtures, native executable, debug information and both inputs
remain unchanged throughout the diagnostic.

## Fused chroma comparison

The existing motion search predicts both complete 8x8 chroma blocks before
calculating their SAD. The variant predicts two rows, accumulates their error
and rejects a candidate as soon as its accumulated luma-plus-chroma error
exceeds the best candidate. A rejected Cb block prevents calculating Cr.
Only a complete prediction can replace the current winner. Candidate order,
tie decisions, interpolation, rounding and border extension are unchanged.

Shared region selection and bilinear interpolation serve ordinary prediction
and the comparison kernel. No heap workspace, reference cache, dependency or
minimum CPU instruction-set change is introduced. The interpolation follows
[H.264](https://www.itu.int/rec/T-REC-H.264-202606-I/en), clause 8.4.2.2.2.

Independent scalar checks cover 12,288 chroma cases at seven error limits, all
64 phases, constant and varying pixels, borders, extreme coordinates, exact
ties, output guards, unchanged inputs and allocation-free execution. The
existing interpolation checks and all 188 complete motion-search fixtures also
pass. These checks run in managed execution, with hardware intrinsics disabled,
and in a strict Native AOT consumer.

Frozen Speed consumers run serially with the preceding CPU and thread settings.
Each row below is a complete 60.041667-second encoding observation. The second
series reverses version order.

| Order | Existing CPU, s | Variant CPU, s |
| --- | ---: | ---: |
| existing-1 | 38.087935 | |
| variant-1 | | 37.599163 |
| variant-2 | | 37.485967 |
| existing-2 | 37.768038 | |
| variant-3 | | 37.590112 |
| existing-3 | 37.902538 | |
| existing-4 | 37.942729 | |
| variant-4 | | 37.654942 |

| Median resource | Existing | Variant |
| --- | ---: | ---: |
| CPU seconds | 37.922634 | 37.594638 |
| Elapsed seconds | 38.778829 | 38.405817 |
| Peak RSS, MiB | 37.82 | 37.32 |
| Cumulative managed allocation, bytes | 2,283,412,972 | 2,283,376,724 |

The CPU reduction is 0.86%, with 1.02% and 0.79% in the two series. The small
RSS difference and effectively unchanged allocation do not justify the added
comparison kernel. The variant is not included in the product.

All eight outputs contain exactly 1,441 packets and have SHA256
`d3090ee699cfdcc4330ec9815adaa1e7b08de1d95530e2e7af2e1596a399e543`.
Every observation reaches EOF with zero retained media storage. Input, source,
fixture and executable hashes remain unchanged. The identical bytes retain the
preceding independent pixel, quality and timestamp verification. No new FFmpeg
ratio or complete-film HLS speedup is established.

## Search-policy implications

The [x264 veryfast preset](https://github.com/mirror/x264/blob/master/common/base.c)
selects subpixel refinement level 2. Its
[macroblock configuration](https://github.com/mirror/x264/blob/master/common/macroblock.c)
enables chroma motion estimation for P pictures only from level 5, and for B
pictures from level 9. Mediana's current search includes chroma error and uses
a different refinement policy. These are different encoder work choices, even
when thread count, frame cadence and nominal rate settings agree.
This public-source comparison does not measure the work performed by each
installed libx264 build.

The exact-output fusion trial does not establish a useful reduction in that
work. Changes to search effort or its rate-distortion objective require complete
CPU, output-size and independently decoded quality comparisons. Disabling a
search component alone does not establish equivalent quality or explain the
complete performance gap.

Subsequent [transform-domain search trials](h264-transform-search.md) failed
quality checks; the complete YUV trial also cost more CPU. A separate complete
work census found no picture or macroblock retries on this HD input.

The subsequent [native interpolation change](h264-native-interpolation.md)
reduces complete encoding CPU by 5.04% with identical encoded bytes. It removes
expanded constant multiplications and short-vector stack transfers; the wider
performance gap remains open.
