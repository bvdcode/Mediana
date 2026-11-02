H.264 deblocking now filters eight positions across an edge together.
Implementation `f65c031` retains the existing boundary-strength classification,
quantizer thresholds and picture, macroblock, slice and edge processing order.

## Exact filtering

The [H.264 recommendation](https://www.itu.int/rec/T-REC-H.264-202606-I/en),
clauses 8.7.2.3 and 8.7.2.4, defines the normal and strong filters.
The implementation applies those equations to eight independent positions with
per-position masks for disabled, normal and strong filtering. Luma and chroma
retain their distinct clipping and side-sample rules. Arithmetic right shifts,
rounding and final clipping preserve the scalar results.

Horizontal bands use exact eight-byte row loads. Vertical bands transpose an
8x8 sample region into the same vector representation, filter it, then transpose
back. Samples outside the filtering region retain their values. The existing
software equations remain in use where SSE2 is unavailable. There is no new
public API, heap allocation or frame-sized intermediate buffer.

For eight-bit input the largest strong-filter weighted sum is 2044. The normal
filter's delta numerator lies between -1271 and 1279, so signed 16-bit vector
arithmetic retains its exact range. Saturating byte packing implements final
sample clipping. Native x64 code contains packed additions, subtractions,
arithmetic shifts, comparisons, transposes and saturating packing. Loading,
filtering, clipping selection and writing helpers are inlined in the edge
operation, avoiding separate calls with intermediate sample vectors.

## Complete decoding measurements

Each operation demuxes and decodes all 1,441 pictures of the 60.041667-second
1280x720 input, then writes all 1,992,038,400 visible YUV bytes. Source and
configuration follow the [stage comparison](h264-stage-performance.md#input-and-measurement).
Frozen Native AOT consumers run sequentially on logical CPU 2, with one media
processing thread and no overlapping build, validation or media workload.
CPU is user plus system time; RSS is peak process resident memory.
These are complete-process observations on a shared Linux x64 host.

| Observation | CPU seconds | Elapsed seconds | Peak RSS, KiB |
| --- | ---: | ---: | ---: |
| before-1 | 27.326701 | 28.687435 | 97,524 |
| after-1 | 24.966651 | 25.707357 | 106,204 |
| after-2 | 24.444634 | 29.334777 | 104,452 |
| before-2 | 27.275225 | 29.261846 | 93,416 |

Paired CPU reductions are **8.64%** and **10.38%**.
Elapsed time improves in the first pair and is slightly worse in the reverse
pair. Peak RSS is higher in both new observations. The filtering operation
allocates no heap storage.
Every raw output matches the independent reference SHA256
`1ae555fcff80cdedd1f41a1d1b9aa86aadcb23884b1e11305d6c1ab5191d2bca`.
Every process reaches EOF with zero owned media-buffer bytes.

Fresh optimized one-thread FFmpeg controls produce the same raw samples:

| Observation | CPU seconds | Elapsed seconds | Peak RSS, KiB |
| --- | ---: | ---: | ---: |
| ffmpeg-1 | 4.092407 | 4.098318 | 66,592 |
| ffmpeg-2 | 3.969315 | 8.458761 | 75,848 |

CPU medians are 24.706 seconds for Mediana and 4.031 for FFmpeg:
a **6.13x** gap remains. External timing also varies between batches;
the paired Mediana observations measure this change's CPU effect.
A complete 1,441-picture encoding control retains the exact MPEG-TS reference,
reaches EOF and releases all owned media storage. This is a correctness control,
not a new encoding throughput comparison. The
[complete-film audiovisual HLS measurements](hls-hd-encoding.md) use the earlier
product sources stated there; the minute-long decoder measurements do not
update that whole-operation ratio.

## Validation

Release build, all 2,960 managed tests, all 699 H.264 tests with hardware
intrinsics disabled, all 30 package assemblies and their metadata/dependencies,
strict package-only Native AOT execution, the native CLI, formatting and root license
headers passed. An actual native CLI preview at 15,000 ms of the real HD source
is byte-identical to the preceding verified consumer.

Shared managed and native tests compare the equations across
all 625 combinations of four boundary strengths, six threshold sets, both
orientations, luma/chroma and minimally sized or padded planes with unaligned
storage guards. They cover mixed masks, signed deltas, clipping, strong-filter
side updates and repeated allocation-free execution.

Additional deterministic cases exercise values immediately below, equal to and
above alpha, beta and the strong-filter threshold. Complete managed, software
and package-only native verification includes those cases.
