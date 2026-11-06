H.264 CAVLC codeword writing indexes an immutable symbol table directly.
The preceding implementation looked up each symbol in a dictionary. Code bits,
lengths, the decoder tree, coefficient analysis and the bit writer are unchanged.
Invalid symbols are rejected before any bits are appended.

## Representation

The 29 normative CAVLC codebooks contain 386 entries. Their nonnegative symbol
numbers extend through 67; the indexed representation uses 410 slots in total.
An unused slot has length zero. Construction rejects duplicate symbols, and
the existing prefix validation still builds the decoder tree. These tables are
initialized once; no per-frame table, cache, buffer or public API is added.

Native x64 disassembly confirms removal of the dictionary lookup call.
The writing method itself grows from 91 to 111 bytes because it contains direct
bounds and membership checks. The code-size change is not a throughput claim.

## Complete encoding

The baseline product revision is `1ddf2e9`; the change is `543d554`. Both Native AOT
consumers process all 1,441 pictures of the 60.041667-second, 1280x720, 24 fps raw input
described in the [stage comparison](h264-stage-performance.md). They use the same QP26,
six-second key-frame interval and 10 Mbit/s rate budget.

Measurements use .NET SDK 10.0.402, Linux x64 Native AOT, Speed compilation,
the default instruction set, logical CPU 2 and `DOTNET_PROCESSOR_COUNT=1`.
Processes run serially in both execution orders. CPU is user plus system time. RSS is peak
resident process memory on the shared host.

| Order and observation | Version | CPU, s | Elapsed, s | RSS, KiB |
| --- | --- | ---: | ---: | ---: |
| First 1 | Before | 33.636151 | 35.477468 | 39,960 |
| First 2 | After | 32.904879 | 38.631470 | 39,468 |
| First 3 | After | 33.074972 | 33.661357 | 38,992 |
| First 4 | Before | 33.732444 | 33.991467 | 38,584 |
| Reverse 1 | After | 33.362727 | 34.294473 | 39,236 |
| Reverse 2 | Before | 33.811728 | 36.336919 | 38,824 |
| Reverse 3 | Before | 33.889491 | 35.969056 | 38,804 |
| Reverse 4 | After | 33.326407 | 38.037654 | 38,448 |

| Median resource over four observations per version | Before | After |
| --- | ---: | ---: |
| CPU, s | 33.772086 | 33.200690 |
| Elapsed, s | 35.723262 | 36.166064 |
| RSS, KiB | 38,814 | 39,114 |
| Cumulative managed allocation, bytes | 2,283,405,384 | 2,283,386,800 |

CPU decreases by 2.06% in the first order and 1.49% in the reverse order;
the pooled median decrease is 1.69%. This is a small local CPU improvement.
Elapsed time does not improve consistently, and there is no meaningful peak
RSS reduction. The original complete-operation CPU and coding-efficiency gaps
remain open.

All eight operations reach EOF after 1,441 pictures and packets, with zero
owned media bytes. Every output contains 78,332,832 bytes and matches the
previously independently verified MPEG-TS reference, SHA256
`d3090ee699cfdcc4330ec9815adaa1e7b08de1d95530e2e7af2e1596a399e543`.
Encoded bytes, decoded samples, timing, quality and bitrate are unchanged.

## Validation

Shared managed and Native AOT checks compare writing against independently
packed bits for dense and sparse symbols, every initial bit alignment, codewords
through 33 bits and 64-bit extremes, mixed sequences, emulation prevention,
trailing bits and optional NAL length prefixes. Invalid symbols leave the bit
position unchanged. Repeated writing allocates no managed storage, and disposal
releases all media storage. Existing VLC reading and CAVLC code references pass.
