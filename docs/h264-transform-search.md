Two transform-domain motion-search variants were checked against independently
decoded output. Neither is included in the encoder. A separate census of the
current encoder also excludes picture and macroblock retries as a cause of the
complete HD encode cost on this input.

## Search criteria

The existing encoder compares YUV sample absolute differences during integer,
half-pixel and quarter-pixel motion search. Vector coding length breaks equal
distortion ties. The alternatives use luma or YUV SAD during integer and
half-pixel search, then re-evaluate the winning prediction using SATD before
quarter-pixel refinement. Half- and quarter-pixel stages each compare four
cardinal neighbors.

SATD sums the absolute coefficients of independent 4x4 Hadamard transforms of
the prediction error, divided by two. It estimates transform-domain residual
cost; it does not calculate the actual quantized residual or CAVLC bit count.
The search objective is `256 * distortion + weight * vector_bits`, with
`weight = round(256 * sqrt(0.85 * 2^((QP - 12) / 3)))`. Vector bits are the
signed Exp-Golomb lengths of both differences from the spatial predictor.

The public [x264 motion-search implementation](https://github.com/mirror/x264/blob/master/encoder/me.c)
uses separate integer/half-pixel and quarter-pixel comparisons with motion
coding costs. The [pixel metrics](https://github.com/mirror/x264/blob/master/common/pixel.c)
include 4x4 Hadamard SATD. The variants tested here do not reproduce x264's
complete mode decisions, rate model or search policy.

Independent matrix multiplication checks the vector SATD calculation. Cases
include constant differences, impulses, opposing checkerboards, identical
blocks and varying samples. Seven rejection limits cover zero, partial error,
the exact result and a non-rejecting maximum. Checks also cover input guards,
unchanged inputs and allocation-free repeated execution.

The luma-only kernel passes 2,048 independent 16x16 cases; the YUV variant
passes 4,096 cases spanning 16x16 and 8x8 blocks. Both pass managed execution,
hardware-intrinsics-disabled execution and Linux x64 Native AOT.

## Complete temporal and rate-controlled sequences

Each variant encodes twelve complete 80-picture sequences, including constant
content, luma motion, chroma motion, variable cadence and a scene change.
Quantizers include 0 and 51; six sequences use constrained rate control.
Fresh unchanged-encoder outputs match all existing decoded references and
every source sample used for the comparison.

All 960 pictures from each variant match independent FFmpeg decoding byte for
byte. Every packet clock and IDR boundary is correct. Complete encoding,
decoding, HLS segment completion and zero retained media storage are checked.
These correctness results do not establish acceptable compression efficiency.

| Case | Luma-only SATD PSNR change, dB | YUV SATD PSNR change, dB |
| --- | ---: | ---: |
| Chroma motion, constrained rate, initial QP26 | -11.983242 | -0.106631 |
| Luma motion, constrained rate, initial QP0 | -2.082668 | -1.686522 |
| Luma motion, fixed QP51 | -1.684006 | -2.160380 |
| Luma motion, fixed QP26 | +0.306829 | +0.262930 |

The luma-only chroma-motion transport grows from 34,592 to 40,796 bytes while
quality falls. Preserving chroma in the YUV objective removes that large loss,
but the other regressions remain. Fixed QP and MPEG-TS size alone do not
establish equal-quality or equal-coded-rate throughput. Neither variant passes
the quality checks for a default change.

## Complete HD compute pilot

The YUV variant also processes the complete 1,441-picture, 1280x720, 24 fps input used
in the [stage comparison](h264-stage-performance.md). Both Native AOT consumers use
`OptimizationPreference=Speed`, the default instruction set, logical CPU 2 and
`DOTNET_PROCESSOR_COUNT=1`. The operations run serially.

| Resource or result | Current encoder | YUV SATD variant |
| --- | ---: | ---: |
| Process CPU, s | 38.787160 | 63.472939 |
| Elapsed, s | 39.733367 | 63.494607 |
| Peak RSS, KiB | 36,380 | 37,704 |
| Cumulative managed allocation, bytes | 2,283,400,208 | 2,281,240,080 |
| MPEG-TS bytes | 78,332,832 | 78,299,932 |
| Coded video packet bytes | 76,055,926 | 76,025,739 |
| Aggregate YUV PSNR, dB | 45.093622 | 44.796614 |

The variant costs 63.64% more CPU and loses 0.297008 dB. Every picture is
independently decoded and compared with the original raw samples; packet
timestamps and all eleven IDR boundaries remain correct. Both operations
reach EOF and release all owned media storage. This is one complete pair,
not a repeated throughput comparison or a new FFmpeg ratio. No extended
timing series is justified by its quality and resource results.

## Picture and macroblock work census

An independently built diagnostic consumer retains the current search and
coding decisions. It counts picture attempts, motion searches, macroblock
quantizer attempts and repeated motion results. Its complete output matches
every byte of the unchanged encoder's 78,332,832-byte MPEG-TS reference.

| Work | Count |
| --- | ---: |
| Input pictures | 1,441 |
| Picture encoding attempts | 1,441 |
| Failed picture attempts | 0 |
| IDR pictures | 11 |
| P pictures | 1,430 |
| Motion searches | 5,148,000 |
| Motion searches on picture retries | 0 |
| Macroblock quantizer attempts | 5,187,600 |

There are 3,600 macroblocks per picture. Motion search runs exactly once for
each P macroblock; macroblock coding runs exactly once for every macroblock.
Repeated rate-control attempts do not explain the measured encode gap on this
input. Instrumented timing is not used as a performance baseline, and these
counts do not establish retry behavior on other content or rate settings.

The [native stack profile](h264-native-profile.md) and this census direct
further work toward the cost of the ordinary compute pass and the efficiency
of coding-mode decisions. Replacing SAD with SATD, excluding chroma or caching
picture retries has not established a quality-preserving speed improvement.
