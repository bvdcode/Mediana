Compiling the current H.264 encoder for the build host's instruction set does
not reduce complete encoding CPU in this comparison. The product retains its
existing CPU requirements and instruction-set configuration.

## Same-source compilation control

Both Linux x64 Native AOT consumers use product sources at `45aeec5`, the same
consumer, .NET SDK 10.0.402, runtime 10.0.13 and
`OptimizationPreference=Speed`. Separate build directories prevent reuse of
compiler outputs across configurations. The only compiler-setting difference
is `IlcInstructionSet=native`; saved compiler responses confirm it.

The selected Intel Core i5-12450H processor supports AVX2. A separate feature
probe compiled with each configuration reports:

| Reported feature | Default | Host instruction set |
| --- | --- | --- |
| SSE2, SSSE3, SSE4.1 | Supported | Supported |
| AVX, AVX2 | Unsupported | Supported |
| Hardware-accelerated Vector128 | Supported | Supported |
| Hardware-accelerated Vector256 | Unsupported | Supported |

These are the feature probes' results, rather than an instruction census of the
encoder. The current encoder contains explicit SSE2 kernels and no explicit
AVX2 kernels. Enabling a wider instruction set does not itself introduce a
wider encoder algorithm.

Microsoft documents that
[`IlcInstructionSet=native`](https://github.com/dotnet/runtime/blob/main/src/coreclr/nativeaot/docs/optimizing.md)
targets instructions supported by the build processor and makes those
instructions a requirement for running the executable. It is therefore a
diagnostic configuration here, not a portable deployment change.

## Complete encoding measurements

Each process reads the complete 1,992,038,400-byte YUV420P8 input from the
[stage comparison](h264-stage-performance.md): 1,441 pictures at 1280x720 and
24 fps, lasting 60.041667 seconds. Both consumers use the existing QP26 and
10 Mbit/s encoder settings, one processing thread, logical CPU 2 and
`DOTNET_PROCESSOR_COUNT=1`. All measured operations run sequentially, without
overlapping builds, tests or other media operations.

| Execution order | Default CPU, s | Host instruction set CPU, s |
| --- | ---: | ---: |
| default-1 | 48.333957 | |
| native-1 | | 54.899750 |
| native-2 | | 51.021714 |
| default-2 | 49.095269 | |
| native-3 | | 55.902303 |
| default-3 | 48.161510 | |
| default-4 | 58.807806 | |
| native-4 | | 59.334496 |

| Median process resource | Default | Host instruction set |
| --- | ---: | ---: |
| CPU, s | 48.714613 | 55.401027 |
| Elapsed, s | 50.691750 | 59.451493 |
| Peak RSS, KiB | 38,678 | 36,690 |

The host instruction-set build uses 13.73% more CPU across these observations.
The separate default/native/native/default series gives 8.72% more CPU; the
reversed series gives 7.73% more. Absolute times vary on the shared host, so
these results do not establish a universal regression percentage. Neither
series supports an encoding acceleration from this compilation setting.
The RSS difference does not explain the CPU gap.

All eight outputs reach EOF with exactly 1,441 pictures and packets and zero
retained media storage. Every MPEG-TS output has 78,332,832 bytes and SHA256
`d3090ee699cfdcc4330ec9815adaa1e7b08de1d95530e2e7af2e1596a399e543`.
These are the previously independently verified encoded bytes. Product,
fixture, input and executable identities remain unchanged during measurement.

Two fresh optimized, one-thread FFmpeg/libx264 controls use 17.576058 and
19.001016 CPU seconds. Both preserve the external reference bytes. Its median
is 18.288537 seconds versus 48.714613 for the default Mediana consumer, a 2.66x
isolated encoding ratio on this input. The encoders retain their different
rate-control and quality results; this is not equal-quality throughput or a
replacement for the complete-film HLS comparison.

## Current native stack diagnostic

Two further complete encodes use the default binary under stopped-stack
sampling, bracketed by two ordinary encodes. All four outputs preserve the
same complete reference bytes and release media storage. Ordinary CPU is
51.489696 and 48.445893 seconds; sampled CPU is 52.995287 and 50.009741 seconds.
Sampled elapsed times of 72.191404 and 68.317237 seconds include debugger
stops and are not throughput measurements. Stop-control wall time is
26.657580 and 25.319773 seconds; it must not be subtracted as though it were a
measured CPU cost.

The two runs produce 400 and 378 selected stack points. Selection uses the
thread with the largest positive CPU-tick delta since the preceding stop.
The original coarse attribution rules give:

| Region | Selected points |
| --- | ---: |
| Motion search, prediction and candidate comparison | 312 |
| CAVLC coefficient writing | 94 |
| Forward transform | 60 |
| Inverse transform | 41 |
| Reconstructed-block writing | 25 |
| Garbage collection | 1 |
| Other | 245 |

These counts are not CPU percentages. Motion search remains the largest named
group, while coefficient and reconstruction work remain visible. Unmatched
points include frame metadata access, coefficient analysis, memory operations,
file operations and 37 stops in a thread-pool wait stack. A thread may have
consumed CPU during the interval and be waiting by the instant of the stop.
The wait stacks therefore do not establish a spinning or asynchronous-I/O
bottleneck. No attribution for the complete competitive CPU difference follows
from these counts.

## Remaining performance work

The previous [CPU-dispatch control](h264-stage-performance.md#diagnostic-cpu-dispatch-controls)
shows that optimized compute kernels materially affect libx264's cost. The
[search-policy comparison](h264-native-profile.md#search-policy-implications)
also identifies different encoder work choices. This instruction-set control
does not separate their contributions or establish which algorithm change
would close the gap. The previous rejected search and reconstruction trials
remain applicable; no search, rate, quality or buffer policy changes here.

The last [complete-film HLS observation](hls-hd-encoding.md) remains 962.836
versus 274.925 CPU seconds, a 3.50x gap. It precedes the latest interpolation
and CAVLC changes. Minute-stage observations do not update that complete-film
result.

Product and fixture sources are unchanged from the passing 3,075 managed,
709 software H.264 and 30-package checks and the complete strict Native AOT
package-consumer validation recorded with
[direct CAVLC encoding](h264-cavlc-encoding.md). Both diagnostic configurations
publish successfully with strict Native AOT warnings. The final Release build
has zero warnings and errors; the root license-header check requires no updates.
