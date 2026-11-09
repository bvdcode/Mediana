# H.264 spatial and temporal motion seeds

`H264MacroblockAnalysis.RateDistortion` starts full-pixel motion search with distinct
spatial and temporal vectors in addition to zero and the median predictor. A
diamond search can otherwise stop at a local minimum even when a neighboring
macroblock already contains a better motion estimate.

The spatial candidates come from the left, top, top-right and top-left macroblocks.
Temporal candidates come from the colocated, right and lower macroblocks of the
previous reconstructed picture. Unavailable and intra-coded neighbors are ignored.
Candidates must refer to L0 reference 0. Integer vectors are clipped to the existing
`MotionSearchRange` and deduplicated in bounded stack storage. No additional picture,
motion history or managed allocation is retained.

Each seed undergoes the existing YUV SAD comparison. Motion-code length still breaks
equal-error ties; diamond and fractional refinement follow the winning seed. The
bitstream contains the selected ordinary motion vector. A temporal seed is a search
hint, including at variable frame intervals, and does not change timestamp or
reference-picture semantics. `Fast` retains its preceding search and output.

The [x264 motion predictor source](https://github.com/mirror/x264/blob/master/common/mvpred.c)
also uses spatial and temporal candidates to initialize search. Encoder search
choices are distinct from the normative prediction and reconstruction defined by
[ITU-T H.264](https://www.itu.int/rec/T-REC-H.264-202606-I/en).

## Complete operation comparison

Linux x64 Native AOT, Intel Core i5-12450H, one execution CPU, CABAC rate-distortion
analysis, in-loop deblocking, a 60-picture GOP and a two-second rate buffer. The
same caller-owned shared memory pool is used before and after. Each process reads
the complete YUV420 input, encodes every picture and writes MPEG-TS. Quality is
measured by independently decoding the complete output with FFmpeg 6.1.1. Payload
is the sum of encoded video packet sizes, excluding MPEG-TS overhead.

| Input | Geometry / pictures | Previous payload | New payload | Previous PSNR | New PSNR | Previous / new SSIM |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| HD30 | 1280×720 / 150 | 2,571,565 | 2,569,338 | 51.014 dB | 51.064 dB | 0.992974 / 0.993053 |
| HD24 | 1280×720 / 288 | 6,078,200 | 6,089,724 | 49.009 dB | 49.096 dB | 0.993201 / 0.993344 |
| Motion | 640×360 / 192 | 1,076,756 | 1,077,032 | 44.256 dB | 44.518 dB | 0.993413 / 0.994176 |
| Flat | 1280×720 / 192 | 80,555 | 80,329 | 71.099 dB | 71.104 dB | 0.999996 / 0.999996 |

Actual payload differs by at most 0.28%. The first three cases use the preceding
matched-payload rates of 3,545,495, 3,683,623 and 961,903 bit/s respectively. Flat
content uses 4 Mbit/s and does not consume that target. Equal nominal rate is not
used as evidence of equal actual size.

Four fresh processes per implementation and input run serially in balanced order,
all at `nice=-20`, the highest ordinary Linux process priority, pinned to the same
execution CPU. CPU includes input reads, encoding, muxing and process exit. Other
workstation activity and frequency changes can still affect these measurements;
the ranges below are material. They do not establish a small CPU improvement or
exact performance parity.

| Input | Previous median CPU (range) | New median CPU (range) | Previous / new median process peak | Previous / new allocated memory |
| --- | ---: | ---: | ---: | ---: |
| HD30 | 5.334 s (5.205–6.818) | 5.297 s (5.134–6.331) | 16.79 / 16.71 MiB | 14.94 / 14.95 MiB |
| HD24 | 8.796 s (8.589–9.773) | 9.236 s (8.635–9.819) | 18.26 / 18.35 MiB | 16.42 / 16.49 MiB |
| Motion | 0.946 s (0.929–0.968) | 0.992 s (0.954–1.069) | 8.93 / 8.95 MiB | 5.25 / 5.25 MiB |
| Flat | 1.122 s (1.095–1.236) | 1.147 s (1.119–1.341) | 16.64 / 16.58 MiB | 14.81 / 14.80 MiB |

Process peaks come from the native consumer's `Process.PeakWorkingSet64`, excluding
the measurement process. Allocations use `GC.GetTotalAllocatedBytes`. Array-pool
and collection timing can vary between fresh processes. Repeated search checks
require zero heap allocations and unchanged input samples and motion state. Every
repeated complete transport matches its independently decoded output hash.

HD24 and motion spend about 5.0% and 4.8% more median CPU for their quality gains.
Speed and quality gaps against x264 veryfast remain open; these results do not
establish universal real-time encoding at 720p30.

## x264 veryfast comparison at the same priority

A separate paired run gives both implementations `nice=-20`, the same execution
CPU and one encoder thread. Each implementation runs in two fresh processes per
input in ABBA order. x264 uses `veryfast`, target rates of 4 Mbit/s for HD and
1 Mbit/s for Motion, an equal maximum rate, a two-second buffer, a 60-picture GOP
and disabled scene-cut IDR insertion. Its other preset tools, including B pictures,
remain enabled. Mediana uses the matched-payload rates above. These are complete
encoding operations from the same source, with MPEG-TS output in both cases.

| Input | x264 / Mediana payload | x264 / Mediana PSNR | x264 / Mediana median CPU | CPU ratio | x264 / Mediana process peak |
| --- | ---: | ---: | ---: | ---: | ---: |
| HD30 | 2,563,313 / 2,569,338 | 51.417 / 51.064 dB | 3.064 / 5.566 s | 1.82× | 129.90 / 16.87 MiB |
| HD24 | 6,073,119 / 6,089,724 | 52.129 / 49.096 dB | 3.061 / 8.243 s | 2.69× | 129.18 / 18.34 MiB |
| Motion | 1,076,721 / 1,077,032 | 47.926 / 44.518 dB | 0.483 / 0.942 s | 1.95× | 71.56 / 8.91 MiB |

Actual payload differs by at most 0.28%. x264 process peaks are measured externally;
Mediana's process peaks use its own native process counter. The memory advantage
does not imply equal quality or CPU performance. Both gaps remain substantial on
HD24 and Motion. The priority setting reduces scheduling competition but does not
isolate memory bandwidth, caches or CPU frequency from the rest of the workstation.

## Validation

The regression corpus contains 22 complete 80-picture sequences, including
QP0/12/18/26/38/51, cropped borders, chroma-only motion, scene changes, variable
cadence, rate retries and enabled or disabled in-loop deblocking. All 1760 pictures
match complete independent FFmpeg reconstruction. Unchanged Fast cases retain
their transport hashes. Sequence checks also cover timestamps, IDR boundaries,
HLS segments, reset, cancellation and release of media buffers.

Direct seeded-search checks cover seven seed positions, four translation
directions, prepared and uncached references, search-range clipping and source
ownership. They include exact translations missed by the preceding local search
and repeated searches without heap allocations.

The complete Release solution passes 6051 tests. A focused 35-test H.264 run also
passes with hardware intrinsics disabled. The strict Linux x64 Native AOT build
and complete native test executable pass. The solution build reports no warnings
or errors; whitespace and license-header verification require no changes.
