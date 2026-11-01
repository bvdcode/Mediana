The H.264 encoder refines its full-pixel motion winner with eight half-pixel
neighbors, then eight quarter-pixel neighbors. It finally examines the original
neighboring-vector predictor. Candidate order, all-plane SAD and motion-code
length tie handling are explicit; this remains a bounded search rather than
global rate/distortion optimization.

## Interpolation and storage

`MotionSearchRange` retains its maximum absolute displacement in luma pixels,
including fractional candidates. Zero retains zero-displacement behavior.
After integer selection, the two refinement stages need offsets within three
quarter samples of that winner in each direction. A prepared neighborhood
contains a 23x23 border-extended byte patch and three 18x18 planes of clipped
horizontal, vertical and diagonal half samples. The original predictor can lie
outside that neighborhood and uses the general interpolation path.

The prepared patch and half planes use 529 and 972 stack bytes. Construction
uses another 1,656 stack bytes for unrounded horizontal sums. No reference-sized
cache or heap object is created. The filter retains the standard coefficients,
clipping and unrounded diagonal calculation.
Quarter positions use upward-rounded integer averages through portable BCL
`Vector128` operations. Cancellation remains checked between search iterations
and refinement stages. Reference pixels and target motion state remain borrowed
and unchanged by selection.

## Complete HD comparison

The input is the complete 60.041667-second cut described in the
[HD encoding measurements](h264-hd-encoding.md): 1,441 pictures at 1280x720,
24 fps. Two fresh Linux x64 Native AOT processes ran sequentially on logical
CPU 2, with initial QP26, a 10 Mbit/s packet target, a two-second capacity and
video-only MPEG-TS output. All builds and other media checks completed before
timing. This is one pair on a shared host.

| Search | CPU | Elapsed | Peak RSS | TS bytes | Aggregate PSNR |
| --- | ---: | ---: | ---: | ---: | ---: |
| Integer | 122.358 s | 124.716 s | 108.48 MiB | 78,284,892 | 39.612 dB |
| Shared half/quarter refinement | 158.189 s | 161.811 s | 101.02 MiB | 78,295,608 | 44.956 dB |

Observed CPU increases by 29.28% relative to integer search for a 5.345 dB
aggregate PSNR increase at nearly the same transport size. The preceding direct
fractional implementation required 288.231 CPU seconds; shared interpolation
uses 45.12% less CPU than that historical run and produces the identical entire
transport file. This establishes no general RSS reduction, repeated throughput
result, perceptual quality or parity with FFmpeg.

SHA256 of the complete fractional output is
`d611f433a2ebf124534cf4f981473e73c61b977b77f954186d63857cc27498e2`.
Whole-file equality preserves the previously independently checked picture
bytes, all PTS/DTS/durations, 31 IDRs, packet capacity and level windows. The
independent decoder and Native AOT consumer agree on all 1,992,038,400 visible
YUV bytes per output and reach EOF without retaining media storage. PSNR uses
exact integer squared errors relative to every decoded source sample.

## Independent regression checks

The extended calibration checks 14,904 search cases and 5,723,136 complete
prediction bytes against direct two-dimensional six-tap and chroma equations.
It includes all 16 fractional predictor phases, both signs, out-of-range
predictors, zero/positive radii, padded input rows and border blocks. A separate
882-block calibration checks all local offsets and extreme signed origins.

The retained regression corpus includes 188 independent complete-search cases
and 1,323 prepared neighborhoods checked against scalar convolution. Guard
bytes, unchanged sources and motion state, original allocation budgets and
zero heap allocation during repeated neighborhood construction/read are checked.
The filter follows H.264 clause 8.4.2.2.1, Table 8-12, retaining unrounded
intermediate sums before diagonal filtering and clipping before quarter averages.

All 21 encoding sequences, including mixed I_PCM/transformed pictures, were
freshly decoded independently before updating their references: 1,070 pictures
and 2,873,448 visible samples match exactly. The separate scaled CLI sequence
also matches independent decoding. Some short sequences lose up to 0.276 dB
PSNR, while others improve; local SAD selection does not guarantee better
whole-sequence squared error after quantization and rate control.

The 2,665-test managed suite, strict Linux x64 Native AOT runner and CLI build
pass. Seventeen focused checks also pass with hardware intrinsics disabled.
The final committed Native CLI reproduces the same complete transport hash in
another full run: CPU 159.789 s, elapsed 163.278 s, peak RSS 96.40 MiB.
A freshly built Native AOT consumer and fresh independent decoding agree on
every visible byte and timestamp of its 1,441 pictures and release media storage
at EOF. These additional checks preserve the scope of the measurements above.

## QP-dependent motion-vector cost trial

A separate trial added a signed Exp-Golomb motion-vector difference penalty to
the combined YUV SAD. It used the current slice QP, including rate-control
retries, and a Q8-rounded weight of `sqrt(0.85 * 2^((QP - 12) / 3))`.
The core lambda relation and square-root scaling for SAD appear in the
[JM reference encoder](https://vcgit.hhi.fraunhofer.de/jvet/JM/-/blob/master/source/app/lencod/lambda.c).
This trial did not reproduce JM's configuration-dependent modifiers or its
complete search and mode decisions. Candidate order, interpolation and storage
were unchanged. Partial SAD stopped only when the candidate could no longer win.

The same complete HD operation was measured in a fresh sequential pair:

| Complete conversion | Current SAD selection | QP-dependent vector cost |
| --- | ---: | ---: |
| CPU seconds | 161.960 | 164.254 |
| Wall seconds | 175.291 | 170.090 |
| Peak RSS, MiB | 101.36 | 101.69 |
| MPEG-TS bytes | 78,295,608 | 78,277,184 |
| Aggregate YUV PSNR, dB | 44.9564 | 44.9351 |

CPU increased by 1.42%, transport size decreased by 0.024%, and aggregate PSNR
decreased by 0.0213 dB. One pair on a shared host does not establish a repeatable
small performance difference. The lower wall time alone is not a CPU improvement.

Independent full decoding and the Native AOT consumer agreed on all
1,992,038,400 visible bytes per output. Every timestamp, duration, IDR boundary,
packet-rate window and level window passed. Complete Annex C HRD conformance
remains open. An independent scalar reference also matched 980 search cases
across five QPs, including extreme predictors, padded rows and image edges;
the same cases passed with hardware intrinsics disabled.

All 21 short sequences matched independent decoding for 2,873,448 sample bytes.
However, one QP51 temporal sequence lost 1.97 dB at the same transport size, and
one sequence with initial QP0 and rate control lost 1.61 dB. This objective was
not adopted: the measured outputs do not support replacing the current SAD
selection and motion-code-length tie handling. Correct local cost calculations
alone do not establish better complete-sequence coding efficiency.

## Vectorized neighborhood filtering

The neighborhood constructor evaluates eight horizontal or vertical filter sums
at once with BCL `Vector128` operations. Unrounded diagonal intermediates remain
32-bit; first-pass sums fit signed 16-bit lanes (-2550 through 10710). The two
remaining columns use the same scalar equations. Storage bounds, filter taps,
clipping, candidate order and motion-vector tie handling remain unchanged.

Eight-byte source loads enter a 128-bit register through scalar creation before
widening the lower lanes. This avoids intermediate stack stores generated by
Native AOT for paired `Vector64` construction. Only eight input bytes are read.
The implementation uses portable BCL operations and also passes with hardware
intrinsics disabled. Filtering follows H.264 section 8.4.2.2.1, equations
8-241 through 8-247.

A complete 60.041667-second audiovisual HLS operation measures source decoding,
1280x720 and 640x360 variants, area resizing, stereo mixing, 96/128 kbit/s AAC,
and transport/playlist writing. Video settings are initial QP26, 10/5 Mbit/s,
a two-second capacity and six-second segments. Fresh Native AOT processes ran
sequentially on logical CPU 2 with `DOTNET_PROCESSOR_COUNT=1` and diagnostic
logging disabled, on the Linux x64 host used for the
[full-film comparison](hls-hd-encoding.md).
This is one controlled pair on a shared host, with the optimized version first.

| Complete operation | Scalar filter | Vectorized filter |
| --- | ---: | ---: |
| CPU, seconds | 191.915 | 182.255 |
| Elapsed, seconds | 206.879 | 192.880 |
| Peak RSS, MiB | 99.89 | 107.25 |
| Published bytes | 119,558,183 | 119,558,183 |

Observed CPU decreased by 5.03% and elapsed time by 6.77%. Peak RSS increased
by 7.37 MiB in this pair; this establishes no memory reduction. The constructor's
explicit working storage and repeated-call heap allocation remain unchanged.
The first SIMD formulation did not improve full-operation CPU in two alternating
pairs; the table measures the revised direct-load implementation.

All 25 published files match byte for byte, preserving encoded quality, clocks,
rate decisions and audio. Each variant contains 1,441 pictures and 2,814 AAC
access units. Both complete playlists and all 22 individual transport segments
were independently decoded to EOF. The tail segments contain video only.

The neighborhood oracle now checks 2,646 candidate blocks across six patterns,
nine origins and all 49 local offsets, including extreme filter-tap patterns,
clipping, border extension and guard bytes. Source preservation, zero heap
allocation and independent complete motion-search results also pass. The 2,923
managed tests, 17 focused tests with hardware intrinsics disabled, all 30 local
packages and the strict Linux x64 package-only Native AOT runner and CLI pass.
This minute-long pair does not replace the complete-film FFmpeg comparison or
establish throughput on other hardware and inputs.

## General interpolation trial

A separate trial applied eight-sample vector filtering to the general luma
interpolation path shared by decoding and reference prediction. It retained
all sixteen fractional phases, scalar tails, existing bounded tile storage,
unrounded diagonal intermediates and clipping before quarter averages.
The extended independent convolution corpus covered 12,672 blocks, including
extreme filter patterns, border extension, unusual widths and tiling.

Two complete 60.041667-second audiovisual HLS pairs used the same settings,
Native AOT binaries, source and logical CPU described above. Execution order
was reversed for the second pair. Each process reached all 1,441 input pictures
and released its media buffers. All 25 output files matched each other and the
previously independently decoded output byte for byte. The trial also passed
2,923 managed tests, 17 hardware-disabled tests and strict package-only Native
AOT execution before measurement.

| Pair and execution order | Current CPU, s | Trial CPU, s | CPU change | Current peak RSS, MiB | Trial peak RSS, MiB |
| --- | ---: | ---: | ---: | ---: | ---: |
| Current, then trial | 177.798 | 174.116 | -2.07% | 102.01 | 118.17 |
| Trial, then current | 187.642 | 190.429 | +1.49% | 98.34 | 106.29 |

The direction of the CPU difference reversed, and peak RSS was higher in both
trial runs. These shared-host measurements do not establish a repeatable
full-operation improvement. The general interpolation change was not adopted.

## Native AOT stage observations

An instrumented build of the retained implementation processed the same complete
minute and reproduced all 25 output files. It counted 2,882 picture encoding
attempts for 2,882 output pictures across both variants, with no rejected
attempts. Rate-control re-encoding did not contribute to this case.

Measured elapsed stage time was 49.419 seconds for video decoding, 13.854 for
resizing, 13.202 for audio processing/writing and 121.732 for video rendition
processing/writing. Within picture encoding, motion search took 56.983 seconds,
block analysis 29.297 and reconstruction/entropy/writing 28.350. These are
instrumented elapsed observations with measurement overhead, not CPU percentages
or an isolated codec benchmark.

The stage observations locate substantial work in both decoding and encoding;
they do not establish each stage's performance relative to FFmpeg. The last
complete-film comparison remains 9.02 times FFmpeg's CPU use, with higher video
error at slightly greater output size. No updated full-film ratio follows from
the minute-long filter measurements.
