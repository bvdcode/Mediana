# H.264 macroblock mode decision measurements

A complete trial of adaptive intra coding in P pictures produced valid streams,
but required 2.08 times the CPU for a 0.54 dB aggregate PSNR improvement on the
measured HD input. A second trial with early screening reduced the search cost,
but retained a rate-controlled quality regression and was also not adopted.
The encoder continues to use P16x16 and P_Skip between IDR pictures.

## Coding model

For every non-skip P macroblock, the trial compared P16x16 with I4x4 using
reconstructed Y, U and V squared sample error plus a quantizer-dependent RBSP
bit cost. Intra4x4 analysis, residual transforms, chroma prediction and CAVLC were
shared with IDR coding. P slices used `mb_type=5` for I4x4, with the existing
I_PCM replacement retained for the 3200-bit macroblock limit.

The P/I macroblock syntax follows H.264 clause 7.4.5 and Tables 7-11 and 7-13.
For P slices, values 5 through 30 select the I table after subtracting 5.
See [ITU-T H.264, August 2021](https://www.itu.int/rec/dologin_pub.asp?id=T-REC-H.264-202108-S!!PDF-E&lang=e&type=items).

The distortion penalty used a Q8-rounded form of
`lambda = 0.85 * 2^((QP - 12) / 3)`. This core relation is present in the
[JM reference encoder's lambda calculation](https://vcgit.hhi.fraunhofer.de/jvet/JM/-/blob/master/source/app/lencod/lambda.c).
The trial used unweighted reconstructed YUV error and did not reproduce JM's
configuration-dependent scaling or complete mode search. This is an encoder
decision model, not a normative H.264 requirement or a perceptual quality metric.

Each candidate used the current macroblock's retained prediction and coefficients.
The losing inter candidate was rewritten after restoring the bit position.
No additional picture-sized pixel backup was introduced. PCM replacement refreshed
the macroblock checkpoint so later candidate rollback preserved the bit writer's
revision checks.

## Complete HD operation

The input was the complete 60.041667-second, 1441-picture, 1280x720, 24 fps
movie excerpt used for the fractional-motion comparison. Both fresh Linux x64
Native AOT processes used the same CPU affinity, initial QP26, 10 Mbit/s packet
ceiling, two-second buffer, two-second IDR interval and video-only MPEG-TS output.
The measurement includes input decoding and output writing.

| Complete conversion | Current encoder | Exhaustive P/I trial |
|---|---:|---:|
| CPU seconds | 162.85 | 339.38 |
| Wall seconds | 167.74 | 345.80 |
| Peak RSS, MiB | 104.57 | 101.52 |
| MPEG-TS bytes | 78,295,608 | 78,010,976 |
| Aggregate YUV PSNR, dB | 44.9564 | 45.4952 |

CPU increased by 108.41%; transport size decreased by 0.36%. These are one
sequential pair on a shared host. The RSS observations do not establish a general
memory reduction, and the result does not establish perceptual quality or an
external-encoder speed comparison.

## Verification

Both full outputs passed independent checks of every packet timestamp, duration,
all 31 IDR boundaries and all packet-rate windows. Independent decoding and a
fresh Native AOT public consumer agreed on all 1,992,038,400 visible sample bytes
per output, reached EOF and retained no media buffers. Canonical MP4 remuxing
preserved every coded picture. The level-window checks passed; complete Annex C
HRD conformance remains open.

Twenty-one short sequences, including moving texture, a scene change, cropped
edges, low-QP PCM blocks, skips and controlled rate budgets, matched independent
decoding for all 2,873,448 reconstructed sample bytes. One CLI conversion also
produced identical managed and Native AOT transport bytes. Fifteen direct mode
cases at QP0, 12, 26, 38 and 51 selected intra blocks after a scene change and
compact skips for the repeated picture, preserving the original reference.

The short rate-constrained cases included a 0.98 dB PSNR regression and a 1.61 dB
improvement. A lower local rate/distortion cost does not guarantee lower error
over a complete rate-controlled sequence. Adaptive intra coding in P pictures
remains open; an efficient decision mechanism needs to avoid exhaustive
transform and entropy trials while preserving independent output verification.

## Selective intra screening

A second trial screened macroblocks before the complete I4x4 transform and
entropy trial. It compared the lambda-weighted actual coefficient bit count
per 384 samples with the source-to-reconstruction discontinuity across the
64 top and left boundary samples. Zero-residual candidates were excluded;
candidates without the required boundaries retained the full trial. Final
selection still compared reconstructed YUV squared error and actual RBSP bits.

This decision follows the selective-intra approach in the
[JM reference encoder](https://vcgit.hhi.fraunhofer.de/jvet/JM/-/blob/master/source/app/lencod/md_highfast.c),
using the weighted coefficient-rate interpretation from its
[rate/distortion implementation](https://vcgit.hhi.fraunhofer.de/jvet/JM/-/blob/master/source/app/lencod/rdopt.c).
The syntax was checked against [ITU-T H.264 V16, June 2026](https://www.itu.int/rec/T-REC-H.264-202606-I/en).
The trial used the existing prediction, residual, checkpoint and PCM coding,
with no additional picture buffer or external runtime dependency.

A fresh sequential pair used the same complete 1441-picture HD source and
settings described above. Both Linux x64 Native AOT processes ran on logical
CPU 2. The baseline includes the constant-residual optimization. These are
separate observations from the earlier exhaustive trial.

| Complete conversion | Current encoder | Screened P/I trial |
|---|---:|---:|
| CPU seconds | 147.59 | 179.08 |
| Wall seconds | 155.25 | 194.11 |
| Peak RSS, MiB | 101.81 | 93.66 |
| MPEG-TS bytes | 78,295,608 | 78,156,676 |
| Aggregate YUV PSNR, dB | 44.9564 | 45.3572 |

CPU increased by 21.33%, size decreased by 0.18% and aggregate PSNR improved
by 0.40 dB. A single pair on a shared host does not establish a general
performance or memory result. PSNR does not establish perceptual acceptance.

For both complete outputs, independent and Native decoding agreed on all
1,992,038,400 visible sample bytes, every timestamp and duration, EOF and zero
retained media storage. All 31 IDR boundaries, level-window checks, packet-rate
windows and coded-picture identities after remuxing passed. Full Annex C HRD
conformance remains open.

All 21 short sequences also matched independent decoding, including 32 pictures
with mixed PCM/cropped edges. The 18-picture managed and Native CLI outputs
were identical. The complete 80-picture, 46x34 horizontal-motion sequence at
initial QP0 and a 32 kbit/s packet ceiling nevertheless regressed from
38.2887 to 37.3057 dB. The vertical-motion/scene-change case at initial QP26
and 40 kbit/s improved from 30.0723 to 31.6779 dB. All rate budgets passed.

The screened trial was rejected as a default because it retained sequence-level
quality regressions while increasing CPU. Efficient adaptive P-picture intra
coding and its interaction with quantizer control remain open.

## Rate-budget diagnosis

The complete 80-picture horizontal-motion case at initial QP0 and 32 kbit/s
was traced through every packet and reconstructed picture. The first changed
macroblock occurs in picture 12. Its reconstructed reference affects later P
pictures and their sizes. At the next scheduled IDR, picture 50, the two paths
have different available packet budgets.

| Picture 50 | Current encoder | Screened P/I trial |
|---|---:|---:|
| Available bits | 2,192 | 1,944 |
| Maximum packet bytes | 274 | 243 |
| Selected QP | 38 | 42 |
| Actual packet bytes | 272 | 229 |
| Visible YUV squared error | 150,519 | 300,438 |

All 52 quantizers were then measured for that same IDR source picture. QP38
is the lowest fitting the 274-byte budget, and QP42 is the lowest fitting
243 bytes. QP41 needs 244 bytes. The affected decision therefore does not
result from skipping a lower fitting QP during retry. The existing hard
packet budget must remain enforced.

Each of the 52 reconstructions matched independent decoding exactly. Both
complete traces also matched independent pixels, every original coded-picture
identity survived diagnostic remuxing, and Linux x64 Native AOT produced
identical traces and all 52 curve outputs.

A separate allocation trial used the known time to the next scheduled IDR
to spread buffer-credit recovery across the remaining interval. It retained
exact clock arithmetic and the existing hard packet limits, without lookahead
or additional picture storage. This was an independent buffer-trajectory
experiment informed by the GOP/frame budgeting in
[JVT-G012](https://www.itu.int/wftp3/av-arch/jvt-site/2003_03_Pattaya/JVT-G012.zip),
not an implementation of its complete quadratic/MAD controller.

All 17 complete short sequences decoded independently. The rate-controlled
cases passed their packet-budget and ownership checks. Quality nevertheless
changed unevenly:

| Complete rate-controlled sequence | Current PSNR | Allocation trial PSNR |
|---|---:|---:|
| Horizontal motion, initial QP0, 32 kbit/s | 38.2887 dB | 40.5487 dB |
| Horizontal motion, initial QP26, 32 kbit/s | 37.0773 dB | 39.9515 dB |
| Chroma motion, initial QP26, 48 kbit/s | 58.6450 dB | 58.2826 dB |
| Scene change with variable cadence, initial QP26, 40 kbit/s | 30.0723 dB | 26.3995 dB |
| Scene change with variable cadence, initial QP51, 40 kbit/s | 26.0490 dB | 22.8846 dB |

The timing-only allocation trial was rejected before long performance testing.
Fixed-QP references were unchanged. Current-frame complexity, reference-error
propagation and quantizer allocation remain relevant to further mode-decision
work; reserving capacity by time alone did not satisfy the quality checks.
