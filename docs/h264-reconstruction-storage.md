H.264 encoding reuses private reconstruction storage instead of allocating new
pixel and macroblock arrays for every picture and rate-control attempt.
Implementation `3a54bd3` is compared with `649977f`.

## Ownership and lifetime

I/P encoding alternates two pictures: the preceding reference and the next
reconstruction workspace. All-IDR encoding needs one workspace. A rate-control
retry resets the same target. The existing media allocator accounts for both
pixel and macroblock storage; no shared pool or unbounded cache is introduced.

Before reuse, neighbor availability, coefficient counts, motion information,
prediction modes, quantizers, transform flags and slice origin are reset. Normal
encoding overwrites the reconstructed pixels. Decoder pictures whose pixels have
been published, disposed pictures and B-picture storage reject this reset before
mutation. Input frames remain borrowed, and retained encoded packets keep their
own immutable data.

An encoder retains its reconstruction workspace between calls. Completing input,
resetting, failure, cancellation and disposal release both private pictures.
Retaining output packets remains the caller's responsibility.

## Complete raw-video comparison

The input and settings match the
[stage comparison](h264-stage-performance.md#input-and-measurement): all 1,441
pictures, 60.041667 seconds, 1280x720 at 24 fps, QP26, a six-second GOP and a
10 Mbit/s setting with a two-second packet buffer. Each process reads the same
1,992,038,400-byte raw reference and writes MPEG-TS. Source decoding, resizing
and audio processing are outside this measurement.

Frozen Native AOT binaries ran serially on logical CPU 2 with
`DOTNET_PROCESSOR_COUNT=1`. Four observations per version used ABBA followed by
BAAB order, with no overlapping build, test or media workload. CPU is user plus
system time; RSS is process peak resident memory. Allocation is cumulative
managed allocation during the operation, rather than simultaneously live memory.

| Execution order | CPU seconds | Elapsed seconds | Peak RSS, KiB | Allocated bytes | Gen2 collections |
| --- | ---: | ---: | ---: | ---: | ---: |
| before-1 | 42.443742 | 46.845078 | 52,284 | 5,238,061,584 | 693 |
| after-1 | 39.369280 | 42.428774 | 38,848 | 2,283,416,304 | 346 |
| after-2 | 39.010660 | 39.764447 | 35,444 | 2,283,414,568 | 352 |
| before-2 | 41.387881 | 43.029978 | 52,484 | 5,238,024,304 | 691 |
| after-3 | 39.534648 | 40.639617 | 37,768 | 2,283,403,248 | 348 |
| before-3 | 39.295191 | 40.329479 | 52,224 | 5,238,034,160 | 695 |
| before-4 | 38.970316 | 39.922585 | 51,084 | 5,238,032,088 | 707 |
| after-4 | 38.583236 | 39.410488 | 39,584 | 2,283,380,896 | 345 |

Cumulative allocation medians decrease from **4.878 to 2.127 GiB, 56.41%**.
Peak RSS medians decrease from **51.03 to 37.41 MiB, 26.69%**. Gen2 collection
medians decrease from **694 to 347**. These reductions occur in both execution
orders.

CPU medians are 40.341536 and 39.189970 seconds, a 2.85% difference. However,
the first series shows 6.50% while the reversed series shows only 0.19%.
Variation in the unchanged baseline prevents claiming a stable CPU improvement.
This change establishes lower allocation and RSS on the measured input.

Every run reaches all 1,441 output packets and releases owned media storage at
EOF. All eight complete 78,332,832-byte transports match the independently
decoded reference, SHA256
`d3090ee699cfdcc4330ec9815adaa1e7b08de1d95530e2e7af2e1596a399e543`.
Compressed samples, timing, output size and decoded quality are unchanged.

## External reference

Two fresh optimized FFmpeg/libx264 runs use the stage comparison's exact command,
the same raw input, logical CPU and one-thread settings, after validation ends.

| Observation | CPU seconds | Elapsed seconds | Peak RSS, KiB |
| --- | ---: | ---: | ---: |
| FFmpeg 1 | 12.368970 | 12.729472 | 113,036 |
| FFmpeg 2 | 12.160437 | 12.864370 | 112,412 |

The CPU median ratio is **3.20x**, 39.189970 versus 12.264704 seconds. The external
files also match their complete independently decoded reference. libx264 still
produces fewer transport bytes, 75,383,112 versus 78,332,832, with higher aggregate
YUV PSNR, 46.098 versus 45.094 dB. The raw input and both encoded file identities
are unchanged from the full-picture quality comparison. These settings do not
establish equal-quality throughput; variation between measurement batches is not
attributed to reconstruction reuse.

## Validation

All 3,073 managed tests and 707 H.264 tests with hardware intrinsics disabled
passed. The new regressions cover I/P and all-IDR reuse, per-picture allocation,
metadata reset, retained packets, protected published pixels, budget exhaustion,
cancellation, reset and release. The shared public-API reuse checks also run in
Native AOT alongside the independent decoded-picture corpus.

Release build completed with zero warnings and errors. Formatting, root
license-header checks, all 30 isolated package checks, package-only strict Native
AOT execution, native sample and CLI builds passed. Source and fixture hashes
remained unchanged throughout validation.

The [complete-film audiovisual HLS measurement](hls-hd-encoding.md) remains a
separate result. Allocation savings do not establish a new full-film CPU ratio.
