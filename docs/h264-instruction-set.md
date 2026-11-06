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

The subsequent [complete-film HLS observation](hls-hd-encoding.md) uses 794.189
versus 195.026 CPU seconds, a 4.07x gap. It includes the later interpolation,
CAVLC and paired-residual changes with the default instruction set. Its 406
output files remain identical to the complete independently verified references;
it does not isolate the effect of an instruction-set choice.

Both instruction-set configurations publish with Native AOT warnings treated as errors.

## Complete decoder dispatch control

A separate control at `8e2a722` compares the unchanged current Native AOT
decoder with FFmpeg 6.1.1 using its default runtime dispatch and two explicit
CPU masks. It uses the complete 60.041667-second High/CABAC input from the
[decoder comparison](h264-cabac-performance.md#current-independent-decoder-comparison):
1,441 pictures at 1280x720/24 fps. Every operation writes all 1,992,038,400 raw
YUV420P8 bytes. All eight outputs have the same independently verified hash;
Mediana also verifies every rational timestamp and duration, EOF and released
media storage.

The serial order is FFmpeg default, Mediana, FFmpeg SSE4.2, FFmpeg SSE2, then
the reverse. Both implementations use one media thread and logical CPU 2;
Mediana uses `DOTNET_PROCESSOR_COUNT=1` and its ordinary GC configuration.
The Native AOT compiler settings and executable are unchanged.

| Decoder dispatch | Median CPU, s | Median elapsed, s | Median peak RSS, KiB |
| --- | ---: | ---: | ---: |
| Mediana | 19.167731 | 20.043059 | 60,614 |
| FFmpeg default | 3.723980 | 4.096801 | 73,152 |
| FFmpeg through SSE4.2 | 3.658337 | 3.724619 | 77,060 |
| FFmpeg through SSE2 | 3.946126 | 3.948271 | 69,466 |

The SSE2 mask is `cmov+mmx+mmxext+sse+sse2`; the SSE4.2 mask additionally
allows `sse3+ssse3+sse4.1+sse4.2`. FFmpeg documents
[`-cpuflags`](https://ffmpeg.org/ffmpeg.html#Generic-options) as a runtime
testing control. These masks constrain kernel selection; they do not establish
identical generated instructions or remove compiler vectorization.

Mediana requires 5.15 times the default FFmpeg CPU and 4.86 times the
SSE2-restricted FFmpeg CPU in this series. Restricting FFmpeg dispatch through
SSE2 adds 5.97% CPU relative to its default, leaving most of the gap. The
small reversal between SSE4.2 and default and the varying absolute times on
the shared host do not establish a general advantage for either mask. Wider
runtime dispatch alone does not explain the observed multiple-times gap.
This is decoding only; it does not update encoding or complete HLS results.
