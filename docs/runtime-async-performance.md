# Runtime Async evaluation

Runtime Async remains opt-in. It reduced packet-reader overhead in this Linux x64
comparison, but did not establish a general improvement in complete conversion or
HLS generation. No production project enables `runtime-async=on` by default.

## Method

SDK 11.0.100, runtime 11.0.0, Intel Core i5-12450H, logical CPU 5,
`DOTNET_PROCESSOR_COUNT=1`. JIT runs disable tiered compilation. Both variants use
Release, the same source and pooled media storage; the candidate additionally
sets `-p:Features=runtime-async=on` for the executable and its project references.
Native symbols confirm runtime-managed continuations in the candidate and
compiler-generated state machines in the control.

Each cell reports the median of three fresh processes, alternating variant order.
Builds and local profiling do not overlap the measurements. Other host workloads
remain active. CPU frequency and scheduling variation are substantial: these are
observations on this host, not isolated estimates of the compiler feature's effect.

The [processing benchmark](../benchmarks/Mediana.ProcessingBenchmarks/README.md)
provides the workloads:

- Packet reading: a 35,694,145-byte MP4 with 4,230 packets, opened and consumed
  300 times from memory or 100 times from an asynchronous file stream. Memory
  loading and initial file opening precede the measured interval. Peak RSS still
  includes the memory input. The file runs observe incomplete asynchronous reads;
  the memory runs observe none. File data is cached by the operating system.
- A/V conversion: a 60.04-second portion of [Big Buck Bunny](real-hd.md), containing
  1,417 video frames and 2,812 stereo AAC access units, converted from 720p to 360p
  H.264/AAC MPEG-TS.
- Additional 720p conversion: 150 frames at 30 fps, preserving resolution.
- Cold HLS: request segment 300 from the complete 596.48-second stereo A/V movie.
- HLS series: generate all eight one-second segments of an A/V input in reverse
  order, with a separate decoder/encoder lifetime for each request.

Conversion and HLS measurements include opening, decoding, preparation, encoding,
output writes and disposal. They use the public encoder defaults: QP26,
CAVLC/Fast, no rate-control target and a two-second keyframe interval. They do not
compare compression efficiency against another encoder.

## Measurements

Values are control / Runtime Async. Allocation is process-wide managed allocation
over the measured operation; peak RSS includes managed and native process memory.
Packet rows cover the complete series, not one packet or one file read.

| Runtime | Workload | CPU ms | Allocated MiB | Peak RSS MiB |
| --- | --- | ---: | ---: | ---: |
| Native AOT | Packets, memory | 1448.2 / 1234.6 | 357.8 / 358.0 | 54.2 / 54.2 |
| Native AOT | Packets, file | 2691.2 / 2317.9 | 471.4 / 388.5 | 21.0 / 21.1 |
| Native AOT | A/V, 60 seconds | 32493.4 / 33992.8 | 73.6 / 72.4 | 41.8 / 42.4 |
| Native AOT | Additional 720p | 4979.8 / 5101.2 | 36.3 / 36.2 | 38.1 / 37.8 |
| Native AOT | Cold HLS segment | 784.0 / 791.3 | 29.7 / 29.9 | 32.8 / 32.8 |
| Native AOT | HLS series | 9619.1 / 7335.2 | 31.8 / 32.0 | 35.4 / 35.3 |
| JIT | Packets, memory | 1688.6 / 1282.8 | 357.3 / 357.2 | 82.1 / 80.8 |
| JIT | Packets, file | 4969.3 / 4602.3 | 428.6 / 368.4 | 49.5 / 50.0 |
| JIT | A/V, 60 seconds | 29693.0 / 31180.4 | 73.9 / 69.4 | 78.8 / 74.2 |
| JIT | Additional 720p | 5257.0 / 5055.9 | 42.3 / 39.1 | 75.8 / 71.3 |
| JIT | Cold HLS segment | 1398.9 / 1351.8 | 29.7 / 29.7 | 68.8 / 66.5 |
| JIT | HLS series | 7516.3 / 7511.2 | 35.5 / 35.0 | 73.7 / 70.8 |

Native AOT packet-reading CPU medians decreased by 14–15%; file-reading allocation
decreased by about 18%. This does not imply the same saving in transcoding, where
packet reading is only part of the operation.

The complete A/V CPU medians increased by approximately 5% in both backends.
Ranges overlap: Native AOT 31.13–57.18 / 31.07–34.78 seconds; JIT
29.52–35.54 / 29.39–32.74 seconds. Native AOT HLS-series ranges also overlap widely,
6.95–11.10 / 7.28–9.28 seconds; its lower candidate median does not establish a
24% HLS speedup. A repeat on an isolated host remains necessary before adopting
Runtime Async globally. No memory reduction is established for all operations.

## Correctness and cold access

All 48 conversion/HLS output sets match byte-for-byte within each workload across
both variants and repeated runs. Every measured operation releases its media
storage. The Runtime Async JIT build passed 658 MP4, HLS, processing and random
access tests, including cancellation, failed-output retry, repeated/out-of-order
requests and rejection of overlapping operations on one session.

Cold HLS opening reads 413,772 bytes of index/header data. Segment 300 then reads
1,651,700 bytes, including decoder preroll; it does not read or encode preceding
segments. These byte counts are identical across both variants.

Independent FFmpeg decoding of the eight-second playlist succeeds without errors:
192 video frames and 384,000 decoded audio samples per channel. Decoded audio
timestamps form a continuous 48-kHz sample grid from zero through eight seconds;
the initial coded AAC priming packet does not add presented audio before zero.

Build controls and candidates into separate artifact directories. For JIT, use
`dotnet build benchmarks/Mediana.ProcessingBenchmarks -c Release`; for Native AOT,
use `dotnet publish benchmarks/Mediana.ProcessingBenchmarks -c Release -r linux-x64
-p:PublishAot=true -p:IlcSingleThreaded=true`. Add the feature property only to the
candidate. Keep input bytes, process settings, iteration counts and encoding
parameters identical.

The complete CABAC/RD operation is covered by a separate
[CPU profile](video-pipeline-profile.md). The feature and supported compilation modes are described in Microsoft's
[.NET 11 runtime documentation](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-11/runtime#runtime-async).
