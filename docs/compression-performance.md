# Span compression and portable vector transforms

The .NET 11 compression update removes stream adapters from buffered PNG encoding,
streams compressed PNG bytes directly into bounded IDAT buffers, reuses a TIFF
inflater between independent strips/tiles, and decodes PDF Flate input directly
into bounded output segments. The compression level, PNG scanline filters,
pixel representation and output quality are unchanged.

## Ownership and validation

TIFF owns one lazy `ZLibDecoder` per image operation and calls `Reset` between
compressed blocks. State is not shared across images or threads. PNG retains its
input during asynchronous output and keeps the existing 64 KiB IDAT workspace;
failure or cancellation does not emit a trailer or continue writing during cleanup.
The obsolete `PngIdatWriteStream` adapter is removed.

`Mediana.IO.BoundedZLibDecoder` reads one complete zlib member, checks its checksum
and enforces the caller's output limit, including a valid empty result at limit
zero. It checks cancellation between bounded input/output steps. A missing trailer,
invalid checksum, incomplete TIFF samples or excess decoded output raises
`InvalidDataException`. Trailing bytes after the first zlib member are ignored,
matching the previous input-stream behavior. PDF prediction remains separate.

The BCL compression workspace is outside Mediana's media-buffer budget, as it was
with `ZLibStream`. No global compression-state cache or additional runtime package
is introduced. PDF still returns an exact-length array and can use temporary
segments plus a final copy; this update does not eliminate those decoded buffers.

## Before and after

The control uses `dc61db85` and the candidate uses the span-based implementations.
Both run on .NET 11.0.0 / SDK 11.0.100, Linux x64, Intel Core i5-12450H, Release,
`DOTNET_PROCESSOR_COUNT=1`, tiered compilation disabled, logical CPU 2 and nice -20.
Five fresh processes per variant alternate order. Warmup lasts at least 500 ms;
each measured series repeats 5–2,000 operations according to image size.
Other host workloads were active.

The [compression harness](../benchmarks/Mediana.CompressionBenchmarks/README.md)
generates deterministic gradient/noise TIFF inputs and Flate PDF image streams.
PNG encoding excludes TIFF input decoding from timing. PDF measurements include
document opening and complete first-page image interpretation, with 16 small
images or one large image. Streaming PNG writes to `Stream.Null` during timing;
hash verification uses a real output buffer outside that interval.

All values below are medians, shown as control → candidate. Allocation counts are
process-wide managed bytes per operation. Peak RSS includes input, warmup, hash
verification and the measured series, not only the live codec workspace.

| Workload | Elapsed ms | CPU ms | Allocated MiB | Peak RSS MiB |
| --- | ---: | ---: | ---: | ---: |
| PNG 64² gradient, buffer | 0.332 → 0.329 | 0.332 → 0.329 | 0.018 → 0.018 | 55.10 → 55.15 |
| PNG 512² gradient, buffer | 15.152 → 18.563 | 14.576 → 17.724 | 1.143 → 1.143 | 51.63 → 51.23 |
| PNG 1280² noise, buffer | 175.209 → 145.706 | 175.225 → 145.716 | 7.133 → 7.132 | 98.88 → 98.32 |
| PNG 64² gradient, stream | 0.271 → 0.320 | 0.271 → 0.320 | 0.063 → 0.063 | 55.87 → 55.57 |
| PNG 512² gradient, stream | 10.394 → 10.756 | 10.395 → 10.757 | 0.063 → 0.063 | 56.44 → 56.16 |
| PNG 1280² noise, stream | 176.773 → 152.990 | 176.789 → 152.949 | 0.063 → 0.063 | 74.45 → 73.96 |
| TIFF 64² gradient, 1-row strips | 0.345 → 0.280 | 0.346 → 0.280 | 0.058 → 0.038 | 54.73 → 54.51 |
| TIFF 512² gradient, 1-row strips | 10.110 → 8.914 | 10.111 → 8.915 | 1.976 → 1.812 | 53.98 → 52.57 |
| TIFF 512² gradient, 64-row strips | 6.494 → 6.378 | 6.481 → 6.363 | 1.759 → 1.756 | 54.16 → 54.28 |
| TIFF 1280² noise, 1-row strips | 37.850 → 33.718 | 37.791 → 33.716 | 11.498 → 11.088 | 133.95 → 127.88 |
| TIFF 1280² noise, 64-row strips | 26.820 → 26.559 | 26.780 → 26.515 | 10.955 → 10.948 | 100.38 → 100.08 |
| PDF, 16 images at 128² | 19.704 → 18.682 | 19.710 → 18.690 | 2.939 → 2.936 | 64.66 → 64.65 |
| PDF, one image at 1024² | 68.198 → 70.014 | 68.205 → 70.016 | 10.098 → 10.036 | 85.60 → 85.13 |

The benefit scales with TIFF block count: managed allocations dropped by 21,336
bytes for 64 strips and about 172,000 bytes for 512 strips. Larger strips already
amortized inflater creation. PDF allocation changes are small and no general PDF
speedup is established.

Timing ranges overlap and some fresh-process PNG comparisons reverse direction.
For example, buffered 512² PNG CPU ranged from 14.01–15.70 ms before and
13.88–23.20 ms after; buffered noise ranged from 140.94–270.02 and 140.98–262.69 ms.
These medians alone do not establish a general PNG speedup or regression.

An additional comparison compiled both PNG implementations into one process,
using the same CRC and frame types, with 12 alternating measured pairs after
warmup and 100 images per batch. Median CPU ms/image:

| PNG workload | Before | After |
| --- | ---: | ---: |
| 64² gradient, buffer | 0.25559 | 0.24582 |
| 512² gradient, buffer | 11.21576 | 10.85015 |
| 64² gradient, stream | 0.27876 | 0.24930 |
| 512² gradient, stream | 10.65367 | 10.30011 |

The tighter comparison shows modest reductions, about 3–11%, with overlapping
ranges. Streaming PNG removes 96 managed bytes per operation; its principal memory
property remains bounded output storage rather than a large allocation reduction.
All 130 fresh-process results have matching output hashes within each workload:
complete PNG bytes, TIFF RGBA pixels and decoded PDF image pixels.

## SIMD transpose

The H.264 4×4 forward transform uses `Vector128.ZipLower`/`ZipUpper` for its matrix
transpose and tests `Vector128.IsHardwareAccelerated`, removing its x86-only gate.
On the measured x64 JIT, the old SSE2 and new portable transform emit identical
192-byte instruction sequences. Both allocate zero heap bytes and produce equal
transforms. No x64 speedup is claimed. ARM64 throughput remains unmeasured.

The comparison executes 8,192,000 transforms per batch in seven alternating pairs.
Timing variation was substantial even with identical machine instructions;
instruction equivalence is stronger evidence here than the observed timing delta.
The existing scalar implementation remains for platforms without accelerated
`Vector128`; independent matrix tests cover extremes and overlapping spans.

## Verification

The complete Release solution builds without warnings or errors. All 6,079 tests
across 15 projects pass, including new exact-limit, missing-trailer, checksum and
independent-strip cases. All 13 forward-transform tests also pass with hardware
intrinsics disabled. The PNG streaming checks cover asynchronous non-seekable
output, pending-write ownership, cancellation and destination failure.
The complete Linux x64 Native AOT test executable passes. Formatting and license
header checks also pass.

Sources: [.NET 11 compression and memory APIs](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-11/libraries),
[portable vector APIs](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-11/runtime),
[BCL ZLibDecoder](https://github.com/dotnet/runtime/blob/v11.0.0/src/libraries/System.IO.Compression/src/System/IO/Compression/ZLibDecoder.cs),
[BCL ZLibEncoder](https://github.com/dotnet/runtime/blob/v11.0.0/src/libraries/System.IO.Compression/src/System/IO/Compression/ZLibEncoder.cs).
