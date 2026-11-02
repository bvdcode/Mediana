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
