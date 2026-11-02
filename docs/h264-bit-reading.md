H.264 bit reading now consumes available bits directly, with a separate path for
reads crossing byte boundaries. Implementation `d66ea64` is compared with
`ec76d3b`, following [block luma interpolation](h264-block-interpolation.md).

## Computation

Single-bit reads use the current byte directly and refill only when exhausted.
Reads of 0–32 bits retain checked bounds and use a shift and mask when the field
fits in that byte. The refill loop, emulation-prevention validation, bit-position
accounting, alignment and RBSP/CABAC endings retain their contracts. The reader
adds no storage, look-ahead or new public API. Native x64 disassembly confirms
the single-bit operation is inlined into the VLC loop; byte refill remains a
separate call. The VLC codebooks and tree are unchanged.

[FFmpeg single-bit reading](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/get_bits.h)
and [CAVLC decoding](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/h264_cavlc.c)
provide comparisons for dedicated bit operations and table lookup. A separate
four-bit transition-table implementation was tested against the same new reader
with the existing VLC tree. The table payload added 83,984 bytes across the
29 codebooks. Complete native decoding, in both execution orders, gave:

| Order | Transition tables CPU, s | Existing tree CPU, s |
| --- | ---: | ---: |
| Tables then tree | 27.185628 | 27.960391 |
| Tree then tables | 27.598373 | 27.346297 |

The table advantage did not repeat in the reverse pair. The additional tables
are not part of the implementation. All control outputs were byte-identical.

## Complete decoding

Each operation demuxes and decodes the same 1,441-picture, 60.041667-second,
1280x720 input and writes all 1,992,038,400 YUV bytes. Configuration and source
identities follow the [stage comparison](h264-stage-performance.md#input-and-measurement).
Frozen Native AOT binaries run sequentially on one assigned CPU, with one media
processing thread and no overlapping build, validation or media workload.
CPU is user plus system time. Measurements are from a shared Linux x64 host.

| Observation | CPU seconds | Elapsed seconds | Peak RSS, KiB |
| --- | ---: | ---: | ---: |
| before-1 | 29.975089 | 30.594819 | 132,992 |
| after-1 | 29.330787 | 30.911430 | 89,200 |
| after-2 | 27.576084 | 29.653459 | 93,280 |
| before-2 | 29.694715 | 30.882368 | 106,608 |

Paired CPU reductions are **2.15%** and **7.13%**.
Elapsed time worsens slightly in the first pair and improves in the reverse pair.
Peak RSS decreases in these pairs but varied substantially across the control
batches; the reader adds no buffers, and no general RSS reduction is established.
All outputs match the independent raw reference, SHA256
`1ae555fcff80cdedd1f41a1d1b9aa86aadcb23884b1e11305d6c1ab5191d2bca`.
Every process reaches EOF with zero owned media bytes.

Fresh optimized one-thread FFmpeg controls produce the same raw bytes:

| Observation | CPU seconds | Elapsed seconds | Peak RSS, KiB |
| --- | ---: | ---: | ---: |
| ffmpeg-1 | 4.501947 | 4.530597 | 67,484 |
| ffmpeg-2 | 4.122324 | 16.768741 | 75,864 |

The ratio of CPU medians is **6.60x** (28.453 versus 4.312 seconds).
The CPU gap remains. Variation in external timing must not be attributed to
this change. No new encoding throughput or complete-film audiovisual HLS result
is established. A complete 1,441-picture encoding control retains the exact
MPEG-TS reference and releases all owned media storage.

## Decoder work distribution

A separate diagnostic of the preceding accepted implementation records
29,946,574 CAVLC residual reads at 3.697 instrumented elapsed seconds.
Luma prediction takes 3.574 seconds, chroma prediction 3.240 and deblocking
5.447, including 3.409 in pixel filtering. Inverse 4x4 transformation accounts
for 1.861 seconds within its enclosing residual stages. All decoded samples
match the reference. These nested timings include instrumentation overhead,
are not CPU percentages, and do not identify a single cause of the entire gap.

## Validation

Release build, all 2,958 managed tests, 697 H.264 tests with hardware intrinsics
disabled, all 30 package checks, strict package-only Native AOT execution,
native CLI, formatting and root license-header checks passed. Shared managed
and native cases cover mixed 0–32-bit reads at all initial alignments,
independent raw bit extraction, reader copies, escaping, exact positions,
trailing/alignment/CABAC padding, long mixed VLC sequences, final-byte codes,
invalid or truncated prefixes and repeated allocation-free decoding.
Native H.264 kernel checks are grouped in a focused component within the
existing source-size analyzer limit.
