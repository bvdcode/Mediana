# Runtime Async evaluation

Mediana enables Runtime Async by default on .NET 11. Repeated Linux x64 measurements
show lower packet-reading CPU cost, including when the consuming application keeps
ordinary compiler-generated async methods. The feature does not replace codec
optimization: complete conversion and HLS remain dominated by decoding, encoding
and frame processing.

An earlier three-process comparison observed approximately 5% higher complete A/V
CPU medians, with widely overlapping ranges. The repeat below did not reproduce
that increase.

## Method

SDK 11.0.100, runtime 11.0.0, Intel Core i5-12450H, Linux x64, logical CPU 3,
process nice -20 and `DOTNET_PROCESSOR_COUNT=1`. Three Release configurations use
identical source and input bytes:

- Control: `runtime-async=off` for the executable and all project references.
- Global: `runtime-async=on` for the executable and all project references.
- I/O only: enabled for `Mediana.IO` and `Mediana.Containers.*`; disabled elsewhere.

The .NET base libraries are unchanged in all configurations. Native symbols
confirm runtime-managed async methods and ordinary state machines in the intended
modules. Each Native AOT workload has six repetitions in all six
variant orderings. JIT packet and cold-HLS workloads use the same six repetitions;
longer JIT operations use three cyclic orderings. Every repetition starts a fresh
process. JIT measurements in this section disable tiered compilation.

Builds and local profiling do not overlap measurements. Other host workloads are
still present: median aggregate CPU utilization is 24.6%; median utilization of
the measured core's SMT sibling is 2.3%, reaching 41.2% in one run. CPU governor
settings are unchanged. These are host-specific observations, not fully isolated
estimates; elapsed time includes scheduling delays and must not be conflated with
process CPU time.

The [processing benchmark](../benchmarks/Mediana.ProcessingBenchmarks/README.md)
provides the workloads:

- Packet reading: a 35,694,145-byte MP4 with 4,230 packets, opened and consumed
  300 times from memory or 100 times from an asynchronous file stream. Initial
  memory loading and file opening precede measurement. Peak RSS includes the
  memory input. Memory reads complete synchronously; file runs observe incomplete
  asynchronous reads. File data is cached by the operating system.
- A/V conversion: a 60.04-second portion of [Big Buck Bunny](real-hd.md), containing
  1,417 video frames and 2,812 stereo AAC access units, converted from 720p to 360p
  H.264/AAC MPEG-TS.
- Additional 720p conversion: 150 frames at 30 fps, preserving resolution.
- Cold HLS: request segment 300 from the complete 596.48-second stereo A/V movie.
- HLS series: generate all eight one-second segments of an A/V input in reverse
  order, with a separate decoder/encoder lifetime for each request.

Conversion and HLS include opening, decoding, preparation, encoding, output writes
and disposal. They use QP26, CAVLC/Fast, pooled media storage, no rate-control target
and a two-second keyframe interval. They do not compare compression efficiency
against another encoder.

## Global configuration

Values are control / globally enabled, reporting medians. Allocation is total
process-wide managed allocation over the operation, not retained storage. Peak
RSS includes managed and native process memory. Packet rows cover the entire
iteration series.

| Runtime | Workload | CPU ms | Elapsed ms | Allocated MiB | Peak RSS MiB |
| --- | --- | ---: | ---: | ---: | ---: |
| Native AOT | Packets, memory | 1095.1 / 986.0 | 1097.5 / 986.8 | 356.1 / 356.1 | 53.0 / 52.9 |
| Native AOT | Packets, file | 1944.3 / 1729.0 | 1944.6 / 1729.1 | 469.5 / 385.5 | 19.9 / 19.8 |
| Native AOT | A/V, 60 seconds | 27621.1 / 27879.8 | 28198.1 / 29132.3 | 66.8 / 67.6 | 37.8 / 39.1 |
| Native AOT | Additional 720p | 4497.4 / 4503.6 | 4497.6 / 4581.6 | 36.3 / 36.2 | 38.0 / 37.9 |
| Native AOT | Cold HLS segment | 728.9 / 732.5 | 757.2 / 732.6 | 29.8 / 29.7 | 33.0 / 32.7 |
| Native AOT | HLS series | 6256.2 / 6229.2 | 6287.6 / 6234.0 | 32.0 / 31.6 | 35.5 / 35.0 |
| JIT | Packets, memory | 1298.5 / 1139.5 | 1298.6 / 1139.5 | 355.3 / 355.3 | 80.9 / 79.7 |
| JIT | Packets, file | 2281.8 / 1935.5 | 2281.9 / 1935.6 | 468.7 / 388.1 | 48.0 / 46.3 |
| JIT | A/V, 60 seconds | 28135.6 / 27524.2 | 28645.7 / 29489.4 | 68.7 / 70.8 | 76.2 / 75.8 |
| JIT | Additional 720p | 4874.0 / 4758.0 | 4926.6 / 4771.3 | 38.1 / 36.1 | 73.3 / 69.6 |
| JIT | Cold HLS segment | 1364.9 / 1274.3 | 1378.3 / 1300.8 | 29.7 / 29.8 | 69.0 / 66.5 |
| JIT | HLS series | 6673.6 / 6516.3 | 6783.4 / 6531.4 | 35.3 / 32.4 | 73.9 / 69.3 |

Packet-reading CPU decreases by 10–11% in Native AOT and 12–15% in JIT. File-reading
allocation decreases by 18% and 17%, respectively. These global results include
changes to the benchmark's own async call chain; they must not be presented as
savings every external application receives automatically.

Complete A/V CPU changes by +0.9% in Native AOT and -2.2% in JIT. Native AOT ranges
are 26.52–30.50 / 26.48–28.89 seconds; JIT ranges are 26.76–28.58 / 27.08–28.71 seconds.
Elapsed A/V medians are approximately 3% higher in both backends, despite the much
smaller or negative CPU difference. The overlapping ranges and remaining host
load do not establish a general conversion speedup or a sustained regression.
Native AOT 720p and HLS CPU medians differ by less than 1%.

The I/O-only configuration reduces packet CPU by 5–6% in Native AOT and 8–11% in
JIT, but does not reduce Native AOT file-reading allocation: 469.5 / 469.4 MiB.
It shows no consistent advantage over global enabling in complete operations.
Keeping a special per-module configuration is therefore not justified by this
comparison.

## Ordinary consuming application

The consumer executable, including its measured byte-source wrapper, is compiled
with `runtime-async=off` in both variants. Only Mediana library projects change.
Native symbols verify this boundary. Three alternating control/library-enabled
pairs run per workload and backend. This JIT series uses the runtime's default
tiered compilation, including startup and warmup inside each measured operation;
its times must not be compared directly with the non-tiered JIT table above.

| Runtime | Workload | CPU ms | Elapsed ms | Allocated MiB | Peak RSS MiB |
| --- | --- | ---: | ---: | ---: | ---: |
| Native AOT | Packets, memory | 1083.4 / 1024.7 | 1083.4 / 1026.7 | 356.1 / 356.1 | 53.1 / 53.0 |
| Native AOT | Packets, file | 1894.8 / 1781.4 | 1895.1 / 1781.7 | 469.5 / 469.5 | 19.9 / 19.8 |
| Native AOT | A/V, 60 seconds | 26188.7 / 26523.4 | 26573.4 / 27423.9 | 66.5 / 66.1 | 37.8 / 37.7 |
| Native AOT | Cold HLS segment | 699.2 / 676.9 | 699.3 / 677.0 | 29.9 / 29.8 | 33.1 / 32.9 |
| JIT, default tiers | Packets, memory | 2170.1 / 2015.8 | 2170.3 / 2015.9 | 355.4 / 355.4 | 80.4 / 79.4 |
| JIT, default tiers | Packets, file | 2960.2 / 2738.7 | 2960.7 / 2742.0 | 464.3 / 466.1 | 45.2 / 44.7 |
| JIT, default tiers | A/V, 60 seconds | 34398.7 / 30604.8 | 34770.1 / 31364.9 | 69.8 / 68.8 | 84.0 / 83.4 |
| JIT, default tiers | Cold HLS segment | 3117.9 / 3125.8 | 3128.3 / 3205.3 | 29.8 / 29.8 | 69.2 / 68.6 |

Packet CPU decreases by 5–6% in Native AOT and 7–8% in default-tiered JIT without
changing the consumer's compilation. Allocation stays essentially unchanged;
there is no 18% allocation saving in this mixed compilation case. Native AOT A/V
CPU changes by +1.3%; default-tiered JIT A/V CPU decreases by 11%. The latter
includes JIT startup and tiering effects and does not establish the same saving
for already-warm services or Native AOT applications. Host CPU utilization during
this series has a median of 21.7%; SMT-sibling utilization has a median of 1.3%.

## Correctness and cold access

Across both repeat series, 141 conversion/HLS output sets match byte-for-byte
between configurations and backends, and match the earlier outputs. The other 96
runs read the same complete packet series; packet, payload and source-byte counts
match. Every measured operation releases its media storage.

The default-enabled Release solution builds without warnings or errors and passes
all 6,086 tests across 15 test projects, with no skips. Formatting and license
header checks pass. The complete Linux x64 Native AOT suite also passes with
ordinary async compilation for the test executable and Runtime Async enabled for
Mediana libraries; native symbols verify this mixed configuration. These checks
include cancellation, failed-output retry, repeated/out-of-order HLS requests and
rejection of overlapping operations on one session.

The default-enabled Linux x64 Native AOT CLI also completes MP3-to-WAVE and
PDF-to-PNG conversions. FFmpeg decodes both outputs without errors.

Cold HLS opening reads 413,772 bytes of index/header data. Segment 300 then reads
1,651,700 bytes, including decoder preroll; it does not read or encode preceding
segments. These byte counts are unchanged by the compilation mode.

Independent FFmpeg decoding of the matching eight-second playlist succeeds
without errors: 192 video frames and 384,000 decoded audio samples per channel.
Decoded audio timestamps form a continuous 48-kHz sample grid from zero through
eight seconds; the initial coded AAC priming packet does not add presented audio
before zero.

## Reproduction and scope

Build control and candidate into separate artifact directories. For JIT, use
`dotnet build benchmarks/Mediana.ProcessingBenchmarks -c Release`; for Native AOT,
use `dotnet publish benchmarks/Mediana.ProcessingBenchmarks -c Release -r linux-x64
-p:PublishAot=true -p:IlcSingleThreaded=true`. Explicitly pass
`-p:Features=runtime-async=off` to the control and
`-p:Features=runtime-async=on` to the global candidate: the repository default is
now enabled. Keep input bytes, process settings, iteration counts and encoding
parameters identical.

For an ordinary-consumer comparison, compile the benchmark with
`runtime-async=off` in both builds and switch only library projects. An MSBuild
import through `CustomBeforeMicrosoftCommonTargets` can set `Features` per project
without changing production files. Confirm the selected modes in native symbols
before measuring. Do not compare a consumer-only switch against a global switch
as if both changed the same code.

The public Task/ValueTask API is unchanged. Consumers do not need to enable the
compiler feature to call Mediana; enabling it in their own projects may remove
additional async-chain overhead. Windows, ARM64, concurrent service throughput
and long-running default-tiered JIT behavior are not measured here.

The complete CABAC/RD operation is covered by a separate
[CPU profile](video-pipeline-profile.md). The feature and supported compilation
modes are described in Microsoft's
[.NET 11 runtime documentation](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-11/runtime#runtime-async)
and [runtime async design](https://github.com/dotnet/runtime/blob/main/docs/design/specs/runtime-async.md).
