At `a1502b9`, CABAC prediction syntax history uses at most two macroblock rows instead
of the complete picture. The scratch size is proportional to coded width;
reconstructed picture metadata remains available for reference pictures,
temporal prediction and deblocking.

The left/top context dependencies follow ITU H.264 (06/2026), clauses
9.3.3.1.1.6–7 ([specification](https://www.itu.int/rec/T-REC-H.264)).
Each slice owns independent history. Entering a macroblock row
clears its band before decoding skip, direct, intra or inter syntax. L0/L1
remain separate. A one-row picture keeps only that row. All scratch bytes
remain charged to the existing allocator; publication and buffer leases do
not change.

| 1280x720 syntax history per slice | Before, bytes | After, bytes |
| --- | ---: | ---: |
| P | 230,400 | 10,240 |
| B | 576,000 | 25,600 |

This is auxiliary syntax memory, independent of the decoded picture buffer.
It introduces no pool, decoded-picture copy, worker or public API.

## Complete decoding measurements

Linux x64 Native AOT consumers demux and decode all 1,441 pictures of each
60.041667-second 1280x720/24fps input. The CABAC input is the High-profile
source from [the complete CABAC comparison](h264-cabac-performance.md); the
CAVLC input is the movie excerpt from [the stage comparison](h264-stage-performance.md).
The baseline includes [SIMD bidirectional weighting](h264-weighted-prediction.md).

Each source has four fresh processes per variant in ABBA and BAAB order,
16 complete operations total. All processes use logical CPU 2,
DOTNET_PROCESSOR_COUNT=1, one media thread, default GC and sequential workloads.
No build or test runs during timing. Every raw sample, timestamp and duration
matches the independent reference, and every operation reaches EOF and
releases all owned media.

| Input | CPU before/after, s | Managed bytes allocated before/after | Peak RSS before/after, KiB |
| --- | ---: | ---: | ---: |
| CABAC | 20.622671 / 20.794865 | 4,191,581,412 / 3,548,167,792 | 62,500 / 61,786 |
| CAVLC | 18.880472 / 19.070512 | 3,377,372,308 / 3,377,371,252 | 69,652 / 69,368 |

Values are medians. CABAC cumulative managed allocation falls 15.35%, about
614 MiB across the full minute. This measures allocation traffic, not a 15.35%
reduction in live or resident memory. CPU changes +0.83% and +1.01%; no
sustained CPU improvement or material RSS improvement is established.
CABAC order changes are +0.80% and -1.20%; CAVLC +3.10% and -0.02%.
The shared host has substantial elapsed-time variation. These results do not
update the FFmpeg ratio or complete HLS encoding comparison.

## Checks

An independent full-picture shadow checks 80 complete history traversals:
vertical wrap, single/multiple rows, slice starts within a row, skipped syntax,
partition sizes, signed differences, distinct lists and exact/insufficient
budgets. The traversal performs zero managed allocations. Managed,
hardware-disabled and Native AOT checks pass.

Exact allocation checks use an explicit non-concurrent GC policy in the two
test executables, a Batch-mode guard and an allocating positive control. The
ordinary decoding measurements above keep default GC. Earlier default-host
suite runs showed intermittent exact-zero failures; standalone Linux x64
.NET 10.0.13 controls reported zero anomalous deltas in 512 empty intervals
under GC pressure in each GC mode, and 64 bytes in each of 32 positive controls.
The cause of the earlier suite failures is not established. Related background
GC accounting is documented [upstream](https://github.com/dotnet/runtime/pull/134855);
the test-host setting follows [the runtime configuration documentation](https://learn.microsoft.com/dotnet/core/runtime-config/garbage-collector#background-gc).
The strict zero-byte assertions remain in place.
