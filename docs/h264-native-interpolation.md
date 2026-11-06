H.264 interpolation retains 128-bit sample vectors through filtering, rounding
and packing. Eight-byte loads and stores transfer exactly the required samples
without intermediate `Vector64` conversions. Encoder search candidates, motion
decisions, quantizers and complete encoded output remain unchanged.

## Compute changes

The default Linux x64 Native AOT code for the previous diagonal filter expands
32-bit vector multiplication by 5 and 20 into four `pmuludq` instructions with
additional shuffles. The filter expresses those operations as
`5 * value = value + (value << 2)` and
`20 * value = (value + (value << 2)) << 2`. These identities preserve the
original 32-bit wrapping arithmetic, including extreme integer inputs.

Rounding still precedes clipping. With SSE2, signed 32-bit to signed 16-bit
saturation followed by unsigned byte saturation implements the same final
clipping to 0 through 255. Half-pixel filtering uses unsigned byte saturation
after the existing signed-halfword rounding. The existing portable vector
equations remain in use when SSE2 is unavailable. No new minimum instruction
set, reference cache or heap workspace is required.

The saturation operations use the BCL
[PackSignedSaturate](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.intrinsics.x86.sse2.packsignedsaturate?view=net-10.0)
and
[PackUnsignedSaturate](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.intrinsics.x86.sse2.packunsignedsaturate?view=net-10.0)
intrinsics. Short sample transfers use
[CreateScalar](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.intrinsics.vector128.createscalar?view=net-10.0)
and unaligned eight-byte reads or writes.

Native disassembly confirms the diagonal core is inlined into neighborhood
preparation without the preceding `pmuludq` sequences. The chroma prediction
method shrinks from 305 to 237 code bytes; the neighborhood constructor grows
from 1,943 to 1,992 bytes while incorporating the diagonal work. Short chroma
loads and stores no longer pass sample vectors through stack temporaries.

## Independent correctness checks

Shared managed and Native AOT checks cover every signed 16-bit input to
half-pixel rounding, including overflow at addition of 16. A further 8,192
independent scalar convolution cases cover signed 32-bit extremes, wrapping
arithmetic, clipping, both four- and eight-sample results and padded strides.
Sample access checks verify writes remain inside the exact eight-byte region.
Repeated kernel execution allocates no managed storage.

Existing scalar luma and chroma interpolation references, all prepared
neighborhood phases and borders, and 188 complete motion-search references
remain exact. These checks pass in managed execution, with hardware intrinsics
disabled, and in a Linux x64 Native AOT consumer.

## Complete-process measurements

Native AOT consumers process the same complete 1,441-picture, 1280x720, 24 fps input
used in the [stage comparison](h264-stage-performance.md). Both use
`OptimizationPreference=Speed`, the default instruction set, logical CPU 2 and
`DOTNET_PROCESSOR_COUNT=1`. Each stage has four observations per version, ordered
before/after/after/before and then after/before/before/after.

| Median process resource | Encode before | Encode after | Decode before | Decode after |
| --- | ---: | ---: | ---: | ---: |
| CPU, s | 38.420480 | 36.485941 | 18.180104 | 17.823533 |
| Elapsed, s | 39.930378 | 38.224383 | 18.551182 | 18.387160 |
| Peak RSS, KiB | 38,794 | 38,518 | 66,228 | 68,404 |
| Cumulative managed allocation, bytes | 2,283,493,540 | 2,283,445,728 | 3,377,180,652 | 3,377,140,864 |

Encoding CPU decreases by 5.04% overall. Separate execution-order series give
5.30% and 5.47% reductions. Decoding's two series give 0.94% and 2.95%; their
small differences do not establish a substantial decoder speedup. The RSS and
allocation observations do not demonstrate a memory reduction.

Every encode produces the exact 78,332,832-byte MPEG-TS reference with SHA256
`d3090ee699cfdcc4330ec9815adaa1e7b08de1d95530e2e7af2e1596a399e543`.
Every decode matches all 1,992,038,400 independent YUV sample bytes. All sixteen
operations process 1,441 pictures, reach EOF and release all owned media
storage.

This measurement establishes a local encoding improvement with unchanged
quality and bitrate. It does not establish a new FFmpeg ratio or complete-film
HLS speedup; the remaining CPU and coding-efficiency gaps stay open.

## Compact first-pass storage

A subsequent change stores the fractional-search neighborhood's unrounded
horizontal filter sums in signed 16-bit scratch instead of signed 32-bit
scratch. For 8-bit input, the positive six-tap coefficients sum to 42 and the
negative coefficients sum to -10. Each first-pass sum is therefore between
-2,550 and 10,710. Pairing symmetric rows stays between -5,100 and 21,420,
within signed 16-bit range. The second pass produces signed 32-bit results
before the existing rounding and byte clipping.

This reduces the 23x18 first-pass scratch from 1,656 to 828 bytes per
neighborhood. Horizontal results can be stored and reloaded directly as eight
16-bit lanes. With SSE2, the diagonal pass uses two
[MultiplyAddAdjacent](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.intrinsics.x86.sse2.multiplyaddadjacent?view=net-10.0)
operations on paired rows; portable vector arithmetic implements the same
equation. The Native AOT neighborhood constructor decreases from 1,992
to 1,795 code bytes. Its generated loop contains the two `pmaddwd` operations.
These code and scratch sizes do not establish a reduction in process RSS.

An independent 64-bit scalar convolution checks 334,904 eight-sample blocks,
including every possible first-pass value at each of six tap positions,
extreme-value combinations, mixed lanes, randomized blocks, padded rows and
unaligned starts. Each input view ends at its last required sample. Checks also
verify unchanged source storage, scalar tail results and allocation-free
repetition. They pass in ordinary managed execution, with hardware intrinsics
disabled, and in Linux x64 Native AOT. Independent complete-neighborhood,
row-rejection and 188 motion-search references also pass in all three modes.

Eight complete encoding observations use the same input, settings, CPU and
serial execution procedure as the preceding comparison. All process 1,441
pictures, reach EOF and release owned media storage. Every output matches the
same 78,332,832-byte independent transport reference.

| Execution order | Before CPU median, s | Compact scratch CPU median, s | CPU reduction |
| --- | ---: | ---: | ---: |
| before/after/after/before | 36.939266 | 35.414328 | 4.13% |
| after/before/before/after | 38.725267 | 36.728315 | 5.16% |

Across all eight observations, CPU medians are 36.939266 and 35.414328 seconds.
Host scheduling varies substantially: individual elapsed observations range
from 36.11 to 91.79 seconds. These local measurements do not establish a
portable elapsed-time improvement. Median RSS is 39,528 versus 38,782 KiB;
cumulative managed allocation is 2,283,517,476 versus 2,283,401,684 bytes.
Neither memory observation establishes a substantial process-memory reduction.
The smaller scratch retains the original candidate order, motion decisions,
quality and output size. The general integer-domain interpolation helpers keep
their existing arithmetic.

A Native AOT CLI conversion with compact scratch storage preserves the preceding HD
transport byte for byte. Independent decoding reads all 49 pictures without errors.
