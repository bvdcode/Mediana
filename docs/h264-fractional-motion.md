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
cache or heap object is created. The existing interpolation filter methods
provide the shared coefficients, clipping and unrounded diagonal calculation.
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
