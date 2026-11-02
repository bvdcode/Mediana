The encoder combines byte subtraction, the forward transform and quantization
for two adjacent 4x4 blocks. Inter luma analysis and chroma analysis use this
paired operation. Eight signed 16-bit SIMD lanes hold both blocks instead of
storing and reloading each block's residuals and transformed coefficients in
32-bit temporary stack buffers.

The shared integer transform and quantizer retain their existing integer
domain. The paired entry accepts byte samples, explicit source and prediction
strides, a quantizer and two coefficient destinations. It returns the original
DC coefficients needed for chroma's separate DC transform. The existing
software arithmetic processes the two blocks when SSE2 is unavailable; no
minimum CPU instruction-set requirement changes.

## Arithmetic bounds

A byte residual lies between -255 and 255. The largest absolute row sum of
the existing transform matrix is six, so either intermediate transform axis
fits signed 16-bit storage: the final coefficient magnitude is at most
`255 * 6 * 6 = 9180`.

Unsigned low and high halves of each 16-bit product form its exact 32-bit
value. The largest quantization factor is 13,107; even combining it with the
largest rounding bias gives `9180 * 13107 + floor(2^23 / 3) = 123118462`,
below the signed 32-bit limit. Across all 52 quantizers and their actual
factors, the largest resulting magnitude permitted by this coefficient bound
is 3,672, which also fits signed 16-bit storage. Sign restoration, scan order,
rounding and chroma's unquantized DC are preserved.

Native disassembly contains four `pmullw` and four `pmulhuw` instructions in
the paired operation, with no `pmuludq` or `pmulld`. The earlier
[single-block bounded pipeline](h264-encoder-work.md#bounded-byte-residual-pipeline-control)
used four 32-bit lanes and SSE4.1 multiplication; its small and order-dependent
CPU difference did not justify adoption. Those earlier observations remain
specific to that implementation.

## Independent checks

A direct matrix multiplication and scalar 64-bit quantization oracle verifies
236,544 block pairs. Random samples cover all 52 quantizers and three source
and prediction stride combinations. Every binary residual corner pattern is
checked at quantizers 0, 26 and 51, with opposite patterns in the adjacent
block. Each pair is checked with both the 16-coefficient luma scan and the
15-coefficient chroma AC scan.

The checks compare every coefficient, both original DC values, source
immutability and output guards. Repeated kernel execution allocates no heap
storage. They pass in managed execution, with hardware intrinsics disabled,
and in a strict Linux x64 Native AOT consumer.

## Complete encoding comparison

Frozen Native AOT consumers encode all 1,441 pictures of the
[60.041667-second HD raw input](h264-stage-performance.md). The baseline uses
product sources at `83ac2c3`. Both consumers use the same public media APIs,
encoder settings, .NET SDK 10.0.402, runtime 10.0.13,
`OptimizationPreference=Speed` and default instruction-set configuration.

Every process runs sequentially on logical CPU 2 with
`DOTNET_PROCESSOR_COUNT=1`. Timed operations do not overlap builds, tests or
other media operations in the comparison. CPU is complete process user plus
system time; RSS is process peak resident memory. Both execution orders and
all observations are retained.

| Execution order | Before CPU, s | Paired CPU, s |
| --- | ---: | ---: |
| before-1 | 36.306649 | |
| after-1 | | 34.411722 |
| after-2 | | 34.355171 |
| before-2 | 36.690433 | |
| after-3 | | 34.281917 |
| before-3 | 36.626748 | |
| before-4 | 36.827647 | |
| after-4 | | 34.321635 |

| Median process resource | Before | Paired |
| --- | ---: | ---: |
| CPU, s | 36.658591 | 34.338403 |
| Elapsed, s | 38.246134 | 35.373519 |
| Peak RSS, KiB | 38,286 | 38,500 |
| Cumulative managed allocation, bytes | 2,283,427,560 | 2,283,371,896 |

Complete encoding CPU decreases by 6.33%. The first execution-order series
gives 5.80%; the reversed series gives 6.60%. These are local shared-host
observations rather than universal percentages. RSS and allocation are
effectively unchanged; no memory reduction is established.

Every observation reaches EOF with exactly 1,441 pictures and packets and zero
retained media storage. All eight MPEG-TS outputs contain 78,332,832 bytes and
have SHA256
`d3090ee699cfdcc4330ec9815adaa1e7b08de1d95530e2e7af2e1596a399e543`.
Their identical bytes preserve the preceding independent decoded-pixel,
quality, timestamp and rate verification. Inputs, product and fixture sources
and frozen binaries remain unchanged during measurement.

This comparison establishes no new FFmpeg ratio or complete-film HLS speedup.
The remaining encoder CPU and coding-efficiency gaps stay open.

## Product validation

Release builds with zero warnings and errors. All 3,076 managed tests and
710 H.264 tests with hardware intrinsics disabled pass. All 30 packages pass
assembly, metadata and package-only dependency verification. The complete
strict Linux x64 Native AOT package consumer passes, including the shared
paired-block oracle. The sample and CLI publish with Native AOT. Formatting
and root license-header checks pass.
