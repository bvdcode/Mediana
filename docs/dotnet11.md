# .NET 11 migration

Mediana targets `net11.0`, with SDK 11.0.100 as the minimum repository SDK.
The release workflow uses .NET 11 for builds, tests, packages and Native AOT
executables. Library consumers need .NET 11; the native CLI includes its runtime.
The x64 runtime baseline is x86-64-v2, including SSE4.2 and POPCNT.

## API and tooling changes

- `Mediana.Core.Memory.BorrowedMemoryStream` and
  `Mediana.IO.ReadOnlyMemoryStream` are replaced by
  `System.IO.ReadOnlyMemoryStream`. Change the namespace at call sites; the
  backing memory still belongs to the caller. These public Mediana types are
  removed rather than retained as duplicate wrappers.
- The custom fixed-capacity PNG stream and unused module references are removed.
  Buffered PNG encoding now uses the span-based `ZLibEncoder` directly over its
  allocated output memory. PNG framing, allocation ownership and cancellation
  remain covered by the existing encoder tests.
- GitVersion.Tool 7 uses `--show-variable` and its migrated configuration schema.
  The old and new configurations produced the same `MajorMinorPatch` on both
  development and release histories at migration. Console logging uses
  Microsoft.Extensions.Logging.Console 11.0.0; tests use Microsoft.NET.Test.Sdk
  18.12.0.
- `Matrix3x2.CreateRotation` now preserves a 0.001-degree rotation that .NET 10
  rounded to zero. Three TGS regression images reflect this behavior. An analytic
  rectangle-edge sampling test verifies the rotation separately; RGB tolerance
  remains one code and alpha comparison remains exact.

## Local validation

Release solution build: zero warnings and errors. All 6,065 tests across 15 test
projects passed. Linux x64 Native AOT tests passed with project references and
again with a package-only consumer of all 57 locally built packages. The native
CLI started successfully. Formatting, license headers, format catalog and release
publication checks passed. Windows and macOS execution remain release-CI checks.

## Before and after measurements

The control was built from `167c87ac` with SDK 10.0.403 / runtime 10.0.14 before
the migration. The candidate uses SDK 11.0.100 / runtime 11.0.0. Both use Release
on Linux x64, Intel Core i5-12450H, one pinned logical processor and
`DOTNET_PROCESSOR_COUNT=1`. JIT measurements disable tiered compilation; Native
AOT builds use each SDK's default instruction-set target.

Five fresh processes per version alternate their execution order. Drawing warms
up first and then measures 64 text, 24 bar or 128 curve renders per process. Each
scene renders at twice the output dimensions, downsamples and encodes lossless
WebP. This series uses logical CPU 5 and process nice level -20. Reported times
and memory values are medians across processes. Other host workloads remain
active; CPU affinity and priority do not eliminate shared-core or frequency effects.

| Drawing workload | Total ms, .NET 10 → 11 | CPU ms, .NET 10 → 11 | Allocated MiB/image, .NET 10 → 11 | Peak RSS MiB, .NET 10 → 11 |
| --- | ---: | ---: | ---: | ---: |
| Text, 1024 × 1024 | 78.92 → 78.65 | 78.78 → 78.52 | 68.412 → 68.310 | 206.1 → 205.4 |
| Rounded bars, 2560 × 2560 | 138.66 → 140.79 | 138.60 → 140.73 | 152.013 → 152.013 | 758.1 → 757.4 |
| Curves, 640 × 480 | 10.59 → 10.36 | 10.46 → 10.30 | 9.397 → 9.398 | 80.8 → 77.6 |

Drawing alone took 41.54 → 41.89 ms, 17.76 → 16.70 ms and 4.55 → 4.54 ms,
respectively. Total-time ranges overlapped: text 77.62–92.80 / 73.12–83.33 ms;
bars 137.74–155.93 / 128.38–151.08 ms; curves 9.73–13.40 / 9.78–10.81 ms.
These results do not establish a general rendering speedup. Allocation is measured
on the rendering thread; peak RSS includes warmup, reference-image export and the
whole measured series, rather than only one live image.

The existing scaler benchmark runs 30 warmup and 120 measured frames per case.
Median times in ms/frame were 9.59 → 9.07 for 1080p→720p, 4.16 → 3.77 for
1080p→360p, 9.71 → 9.82 for 2160p→360p, 4.54 → 4.04 for 720p→480p and
0.308 → 0.276 for the 720p identity case. Run-to-run variation was larger than
these differences, so the measured reductions are not an established speedup.

All drawing and scaler pixel hashes match across both versions and all repeats.
Drawing WebP bytes also match: 200,442, 12,570 and 4,746 bytes respectively.

H.264 uses the Native AOT operation benchmark: a 150-frame 1280 × 720, 30 fps
YUV420 source, QP26, 4 Mbit/s target, two-second rate-control buffer, GOP 60,
CABAC, fast macroblock analysis, enabled deblocking and pooled media buffers.
The measured operation includes Y4M reading, encoding and MPEG-TS output.

| H.264 series | Elapsed seconds, .NET 10 → 11 | CPU seconds, .NET 10 → 11 | Allocated MiB/operation, .NET 10 → 11 | Peak RSS MiB, .NET 10 → 11 |
| --- | ---: | ---: | ---: | ---: |
| Logical CPU 2, normal priority | 5.830 → 5.939 | 5.747 → 5.939 | 15.102 → 15.157 | 23.54 → 23.90 |
| Logical CPU 5, nice -20 | 6.547 → 7.091 | 6.541 → 7.065 | 15.165 → 15.097 | 23.70 → 23.87 |

Each H.264 series has five alternating runs per version. .NET 11's median elapsed
time was 2–8% higher. The ranges are wide: 5.40–6.67 / 5.23–7.37 seconds in the
first series and 5.59–10.00 / 6.64–8.36 in the second. A consistent runtime effect
cannot be separated from host variation here; an isolated-host comparison remains
open. Neither series establishes an H.264 speedup or real-time 720p30 processing.
Allocations use the process-wide managed allocation counter, while peak RSS also
includes runtime/native memory. All runs released their media buffers and encoded
exactly 150 pictures with 2,908,821 bytes of elementary payload.

Source SHA-256:
`acd7b1f14895564dfc33a80926c5bb5f877842f570c6efec61467560b459af47`.
Every MPEG-TS output in both series has SHA-256
`88e80209a832e9deda93754812aa0b923d888031cefafa2aa0fe3404cba1ce51`.

Reproduce with [DrawingBenchmarks](../benchmarks/Mediana.DrawingBenchmarks/README.md),
[ProcessingBenchmarks](../benchmarks/Mediana.ProcessingBenchmarks/README.md) and
[OperationComparison](../benchmarks/Mediana.MediaComparison/README.md), using the
same input, output dimensions, iteration counts and CPU settings for both builds.
The measurements cover this machine and these workloads; they are not a claim
about Windows, ARM64, long-running video or concurrent service throughput.

## Performance opportunities

The runtime and compiler provide new optimizations for bounds-check elimination,
checked arithmetic, vector code generation and Native AOT interface calls.
Eligible code can benefit on rebuild without codec-specific dispatch changes;
the measurements above do not establish an overall performance improvement.

| Feature | Applicable work | Status |
| --- | --- | --- |
| BCL memory streams | SVG/TGS/XML/PDF/TIFF input | Replaced the duplicate stream implementations |
| Span-based ZLib/Deflate/GZip encoders and decoders, with `Reset` | PNG scanlines, TIFF strips, PDF compressed streams and TGS input | PNG/TIFF/PDF implementations and [paired measurements](compression-performance.md) are available; TGS remains a candidate |
| Runtime Async | HLS opens and asynchronous packet/source reads | Enabled by default after [JIT, Native AOT and ordinary-client comparisons](runtime-async-performance.md); reduces packet-reading CPU without establishing a general conversion speedup |
| Portable SIMD `Zip`, `Unzip` and `Concat` | Audio interleaving and video block transposes | H.264 4×4 transpose uses `ZipLower`/`ZipUpper`, with identical x64 JIT instructions; other replacements still require measurement |
| Hardware `Half` operations | Future half-float image/HDR processing | Requires suitable hardware and a matching precision contract; does not replace integer codec arithmetic |

Sources: [.NET 11 overview](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-11/overview),
[runtime changes](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-11/runtime),
[library changes](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-11/libraries),
[GitVersion 7 migration](https://github.com/GitTools/GitVersion/releases/tag/7.0.0).

A subsequent [paired quantization experiment](h264-encoding-matrix.md#paired-quantization-experiment) used `Vector128.ZipLower` to prepare reusable SIMD coefficients. All outputs remained exact, but complete-encoding CPU gains were mixed with regressions, so the original implementation remains in use.
