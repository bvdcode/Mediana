# Decoded H.264 storage

H.264 reconstruction uses separate budgeted buffers for coded YUV pixels and
private macroblock metadata. The pixel allocation is mutable only while the
picture is being reconstructed and filtered. Publication transfers it to a
read-only owner and invalidates writable access. Repeated publication and
`VideoFrame.Retain` create independent leases on the same immutable pixels.

The reference-picture buffer keeps its own pixel lease. Removing a picture from
that buffer releases its reconstruction metadata, while returned frames remain
readable. Resetting or disposing a decoder cannot invalidate retained frames.
The allocator releases the pixel charge only after the last owner is disposed.
Failed picture construction releases both allocations; cancellation before
publication leaves the picture writable.

This follows the explicit owner and consumer lifetimes in
[Microsoft's memory guidelines](https://learn.microsoft.com/en-us/dotnet/standard/memory-and-spans/memory-t-usage-guidelines).
Reference-counted decoded picture buffers are also part of
[FFmpeg's decoder buffer contract](https://ffmpeg.org/doxygen/trunk/structAVCodecContext.html).

## Visible planes

`VideoFrame.Data` contains the complete coded YUV allocation. It excludes private
macroblock metadata. Crop offsets, coded strides, visible widths and visible
heights are declared separately in `VideoFrame.Planes`. A cropped frame can have
padding between visible rows and planes. The frame's format, color description,
sample aspect ratio, timestamp and presentation duration are preserved.

Consumers read a visible row with `frame.GetRow(plane, row)` or use the declared
plane offset and stride. The whole `Data` allocation is not a tightly packed
visible image. Explicit conversion or serialization can allocate a different
layout when required by its destination.

## Verified contracts

Managed and Native AOT checks cover I/P/B picture storage, cropped and uncropped
layouts, every visible sample, a completely occupied media budget, repeated
publication, retained frames, separate reference/frame disposal, cancellation,
write rejection and failure during either picture allocation. Shared frames
retain only coded pixels after the reference picture is disposed.

## Complete HD operation

A Native AOT consumer decodes all 1,441 pictures of a 60.041667-second 1280×720
input, selects the complete interval starting at zero, and writes every visible
YUV row. The source is the [same original minute](h264-stage-performance.md) used
for the stage comparisons, SHA256
`227becb9f6f3d2803eaa93095b692ccea9110ca00d78e65872e812d3c00b1692`.
Each complete output contains 1,992,038,400 bytes, all matching independent decoding.

Before/after/after/before processes ran serially on logical CPU 2 of the same
Linux x64 host, using .NET SDK 10.0.402 and `DOTNET_PROCESSOR_COUNT=1`. No owned
build, test or other media operation overlapped measurement. The table reports
medians of two observations per version; CPU is user plus system time.

| Resource | Separate output copy | Shared published pixels |
| --- | ---: | ---: |
| CPU, seconds | 24.334 | 23.209 |
| Elapsed, seconds | 26.201 | 26.184 |
| Peak process RSS, MiB | 99.67 | 65.79 |

CPU falls by 4.62% in this batch. Elapsed time is effectively unchanged. RSS is a
process observation affected by GC and runtime state, rather than the buffer
allocator's live-byte counter. These observations do not establish a universal
CPU or RSS reduction.

A separate, identical package-only consumer hashes every visible sample and
records managed allocations and live media after each returned frame. It uses
the same full input and independent pixel hash. Two observations per version
give cumulative allocations of 5.370 → 3.377 GB, a 37.10% reduction.
This is allocation traffic across the complete operation, not simultaneous
process memory. Sampled live-media maxima fall from 12,441,917 to 9,677,117 bytes;
these samples do not measure temporary peaks inside a decode call. All media
charges return to zero after session disposal.

Two fresh, complete FFmpeg decode processes use one decoder/output thread, one
filter thread and the same CPU affinity. Every output byte matches. Their median
CPU time is 3.527 seconds versus 23.209 for the public Mediana consumer, a
6.58x difference. The video CPU gap remains open. This change does not establish
a new encoding or complete-film HLS throughput result.

All four CFR, VFR, edited and HD full transcodes and their selected windows remain
byte-identical to the preceding outputs and independently decode with the same
presentation times. The original film's 540–546 second transcode and actual
cropped/HD CLI previews also remain byte-identical. Release compilation, format,
license headers, 2,976 managed tests, 700 H.264 checks without hardware intrinsics,
30 isolated package references, strict Native AOT execution and the CLI pass.

## Synchronous reconstruction access

Writable reconstruction pixels and macroblock metadata use
`WritableMediaBuffer.Span` directly. This checked synchronous view borrows the
same allocation as `Memory`; the existing owner validation still rejects access
after publication or disposal. Asynchronous I/O continues to use `Memory`.
This change adds no cached array, reference-plane workspace or frame copy.

The frozen Linux x64 Native AOT code for mode access decreases from 139 to 86
bytes, coefficient-count access from 434 to 272 bytes, and writable-plane access
from 218 to 154 bytes. Their generic `Memory.Span` interpretation paths disappear.
The owner's storage-validation method remains unchanged. The presence of a
generic indirect-call path in the preceding code does not prove that the path
was executed for array-backed buffers.

Frozen consumers compare complete decoding and encoding of the same
1,441-picture, 1280x720 minute input. The baseline already contains
[compact interpolation scratch](h264-native-interpolation.md#compact-first-pass-storage).
Both versions run serially on logical CPU 2 with `DOTNET_PROCESSOR_COUNT=1`;
CPU includes user and system time. No owned build, test or other media workload
overlaps measurement. Each table row contains two observations per version.

| Operation and order | Before CPU median, s | Direct span CPU median, s | CPU reduction |
| --- | ---: | ---: | ---: |
| Decode, before/after/after/before | 19.342899 | 17.955264 | 7.17% |
| Decode, after/before/before/after | 22.265763 | 20.642579 | 7.29% |
| Encode, before/after/after/before | 35.466529 | 33.712926 | 4.94% |
| Encode, after/before/before/after | 34.718658 | 33.791355 | 2.67% |

Across both orders, complete-process medians are:

| Resource | Decode before | Decode direct span | Encode before | Encode direct span |
| --- | ---: | ---: | ---: | ---: |
| CPU, s | 19.993417 | 18.504284 | 34.718658 | 33.791355 |
| Elapsed, s | 20.719284 | 18.981822 | 36.277639 | 35.071204 |
| Peak RSS, KiB | 69,142 | 68,814 | 38,248 | 38,344 |
| Cumulative managed allocation, bytes | 3,377,466,452 | 3,377,427,148 | 2,283,431,400 | 2,283,418,676 |

These memory observations do not establish a substantial process-memory
improvement. Resource differences are local observations on a shared host.

All sixteen operations process every picture, reach EOF and release owned media
storage. Every decoded output matches the independent 1,992,038,400-byte raw
reference, and every encoded output matches the 78,332,832-byte transport
reference. Product sources, fixtures and executable hashes remain unchanged
during timing. This is a local access-path improvement; it establishes no new
FFmpeg ratio or complete-film HLS throughput result.

The shared buffer checks cover zero initialization, identity of span and memory
views, writes through either view, publication, retained sliced leases, released
owners and allocation-free repeated access. All 3,114 managed tests and 719
H.264 tests with hardware intrinsics disabled pass. All 30 packages pass
assembly, metadata and package-only dependency verification; the strict Linux
x64 Native AOT consumer passes its complete run. The sample and CLI publish
with Native AOT. An actual CLI HD conversion preserves the preceding transport
exactly, and independent decoding reads all 49 pictures without errors.
Release compilation has zero warnings and errors; formatting and root license
header checks pass.

## Decoder metadata reuse control

A separate decoder-private cache retained one retired metadata allocation.
Published pixel leases remained independent, and all cached bytes stayed charged
to the media allocator. Retired picture owners rejected further access; reused
storage was cleared before a new picture acquired it. The encoder retained its
existing workspace policy, but the shared picture access guard also affected it.

The control uses the same complete 1,441-picture HD input and frozen Native AOT
consumers as the synchronous-access comparison above. Before/after/after/before
operations run serially on logical CPU 2 with `DOTNET_PROCESSOR_COUNT=1`, with no
owned build, test or other media workload overlapping timing. Each table entry
is the median of two complete processes per version. CPU is user plus system.

| Resource | Decode before | Decode with cache | Encode before | Encode with shared guard |
| --- | ---: | ---: | ---: | ---: |
| CPU, s | 18.293961 | 18.478476 | 45.868709 | 46.105159 |
| Elapsed, s | 18.974647 | 18.728401 | 50.067046 | 48.628052 |
| Peak RSS, KiB | 68,452 | 69,748 | 37,390 | 38,902 |
| Cumulative managed allocation, bytes | 3,377,445,520 | 3,297,722,072 | 2,283,481,516 | 2,283,460,188 |

Decode allocation traffic decreases by 2.36%, while observed CPU is 1.01%
higher. Encoding CPU is 0.52% higher and its allocation traffic is effectively
unchanged. The encoding observations span 35.25–56.49 CPU seconds across the
batch, so these small median differences do not isolate a causal CPU regression.
No useful acceleration or peak-memory reduction is established. The variant is
not adopted; no second execution-order batch was performed.

A separate counter-instrumented complete decode explains the limited reuse:

| Event | One-list metadata, 669,600 bytes | Two-list metadata, 1,188,000 bytes |
| --- | ---: | ---: |
| Requested pictures | 781 | 660 |
| Reused allocations | 119 | 0 |
| Idle allocations evicted for incompatible size | 600 | 660 |
| Returned allocations discarded because the slot was occupied | 61 | 0 |

The counter run also reproduces every independent raw output byte. It is a
lifetime census, not a performance measurement. A single exact-size slot
repeatedly discards storage as the picture layouts alternate. Increasing cache
capacity also requires accounting for idle storage under allocation pressure;
output-frame disposal alone cannot determine metadata reuse because reference
pictures can still require it.

All eight timed operations reach EOF, release their media charges and match the
independent raw or encoded transport reference byte for byte. Sources, fixtures
and executable hashes remain unchanged during measurement. The isolated variant
passes 3,119 managed tests, 724 H.264 tests with hardware intrinsics disabled,
and strict Native AOT ownership and arithmetic checks. These checks include
retained published pixels, stale-owner rejection, reset state, bounded leases,
allocation failures and changes of geometry and reference-list count. They
establish correctness of the tested variant, without establishing a useful
performance improvement or a new complete-film HLS result.

## Measured allocation cost

The separate [complete-operation allocation diagnostic](h264-allocation-cost.md)
observes about 0.255 seconds inside buffer allocation calls for full decoding and
0.262 seconds for full encoding. GC pauses are about 0.149/0.158 seconds.
These elapsed intervals overlap and do not measure attributed CPU. They do not
support treating a buffer pool as the main remedy for the multi-fold CPU gap.

## Per-operation metadata borrowing control

A separate two-file control borrows the existing checked mode span once within
a synchronous operation. Motion and intra-neighbour reads reuse an already read
mode value; motion writes and reconstructed-block availability updates reuse a
method-local span. The borrow does not outlive the operation. The existing
buffer owner, slice and partition checks, metadata layout and pixel ownership
remain unchanged. No pool, retained array or public API is added.

The frozen Linux x64 Native AOT functions become smaller:

| Function | Before, bytes | Local borrow, bytes |
| --- | ---: | ---: |
| Motion partition write | 761 | 694 |
| Reconstructed-block write | 457 | 376 |
| Motion sample lookup | 605 | 587 |
| Intra prediction mode | 307 | 293 |
| Intra-neighbour availability | 231 | 172 |

The motion-write and reconstructed-block loops move checked storage access out
of their cell loops. This static reduction does not establish lower process CPU.
The complete-input comparison uses the same 1,441-picture references, CPU 2,
`DOTNET_PROCESSOR_COUNT=1`, serial processes and no overlapping owned heavy
workloads as the synchronous-access comparison above. Each row reports medians
of two complete processes per version; CPU includes user and system time.

| Operation and order | Before CPU, s | Local borrow CPU, s | Observed CPU change |
| --- | ---: | ---: | ---: |
| Decode, before/after/after/before | 18.614428 | 23.731872 | +27.49% |
| Decode, after/before/before/after | 18.352584 | 18.789542 | +2.38% |
| Encode, before/after/after/before | 34.694918 | 39.246704 | +13.12% |
| Encode, after/before/before/after | 34.317315 | 33.630942 | -2.00% |

The trial's individual decoding observations range from 17.92 to 29.54 CPU
seconds; encoding ranges from 33.30 to 42.83. Across both orders, CPU medians
are 18.389928 → 18.789542 seconds for decoding and 34.694918 → 34.813789 for
encoding. Cumulative allocation and peak RSS do not establish a useful memory
improvement. Smaller native functions and fewer repeated owner checks do not
produce a consistent complete-operation acceleration here. The variant is not
adopted.

All sixteen complete outputs match the independent raw or transport reference,
reach EOF and release owned media storage. Source, fixture, input and executable
hashes remain unchanged during timing. The isolated consumer publishes with
strict Native AOT warnings. This control establishes no new FFmpeg ratio or
complete-film HLS result.
