Repository Native AOT builds default to `OptimizationPreference=Speed`.
An explicit MSBuild property can select another preference. This setting is
local to repository builds; applications using the NuGet libraries select their
own publication properties.

The [.NET optimization documentation](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/optimizing)
describes the blended compiler default and the speed preference. The setting
does not raise the minimum CPU instruction set or enable AVX2 on every machine.
Compiler arguments confirm `--Ot` without an instruction-set override.

## Complete stage comparison

Two frozen strict Native AOT consumers use identical product and consumer
sources from `edb7bae`, SDK 10.0.402, runtime 10.0.13 and the default minimum
instruction set. Only the optimization preference differs. The input is the
complete 1,441-picture, 1280x720, 24 fps, 60.041667-second sequence from the
[stage comparison](h264-stage-performance.md).

Encoding reads the independent raw YUV420P8 input and writes MPEG-TS. Decoding
reads the original MOV and writes all visible raw samples. Each operation uses
one processing thread, logical CPU 2 and `DOTNET_PROCESSOR_COUNT=1`. Processes
run sequentially, without an overlapping owned build, test or media workload.
The second series reverses implementation order. CPU is user plus system time.

| Order | Encode default CPU, s | Encode Speed CPU, s | Decode default CPU, s | Decode Speed CPU, s |
| --- | ---: | ---: | ---: | ---: |
| default-1 | 39.700259 | | 19.528827 | |
| speed-1 | | 36.568906 | | 17.800685 |
| speed-2 | | 36.672681 | | 17.551275 |
| default-2 | 39.440021 | | 21.429826 | |
| speed-3 | | 38.694971 | | 37.537687 |
| default-3 | 39.959911 | | 56.259788 | |
| default-4 | 40.933317 | | 49.833711 | |
| speed-4 | | 37.694554 | | 34.918235 |

| Median resource | Encode default | Encode Speed | Decode default | Decode Speed |
| --- | ---: | ---: | ---: | ---: |
| CPU, s | 39.830085 | 37.183618 | 35.631769 | 26.359460 |
| Elapsed, s | 42.697725 | 39.921104 | 37.201387 | 29.193723 |
| Peak RSS, MiB | 35.137 | 36.289 | 58.807 | 63.344 |
| Cumulative managed allocation, bytes | 2,283,452,936 | 2,283,452,228 | 3,377,354,280 | 3,377,295,824 |

Encoding CPU medians improve by **6.64%**. The first and reversed series show
7.45% and 5.57%. Decoding improves in both series, but CPU changes from roughly
18-21 seconds in the first series to 35-56 seconds in the second. Its aggregate
26.02% reduction does not establish a stable improvement magnitude on this
shared host. Peak RSS does not improve; allocation is effectively unchanged.
The consumer executable grows from 2,971,912 to 3,192,904 bytes, or 7.44%.

All eight encoding outputs are exactly the independently decoded reference:
78,332,832 bytes, SHA256
`d3090ee699cfdcc4330ec9815adaa1e7b08de1d95530e2e7af2e1596a399e543`.
All eight decoding outputs contain exactly 1,992,038,400 bytes, SHA256
`1ae555fcff80cdedd1f41a1d1b9aa86aadcb23884b1e11305d6c1ab5191d2bca`.
Every observation completes 1,441 pictures, reaches EOF and releases owned
media storage. Product sources, reference fixtures, inputs and frozen
executables remain unchanged throughout measurement. Identical encoded bytes
retain the preceding independent timing and quality verification.

## External controls and remaining work

Two fresh optimized one-thread FFmpeg/libx264 controls produce the same complete
reference outputs as preceding external measurements. Encoding uses 25.811608
and 25.865392 CPU seconds; decoding uses 5.644392 and 6.391373 seconds.
The preceding external encoding median was 12.264704 seconds. This large change
in the external control, together with the native decoder variation, prevents
using these serial batches to claim a narrowed competitive gap.

The setting improves the measured encoding configuration without changing its
algorithm, compressed bytes or decoded samples. It does not resolve the
remaining motion-search, coding-efficiency and complete-film HLS performance
requirements. The separate [joint candidate trial](h264-candidate-comparison.md)
is not included: its added implementation was rejected.

The Native AOT sample self-test, CLI version, MOV metadata probe and MOV-to-PNG
preview pass; the preview has dimensions of 80x48.
Explicit `OptimizationPreference=Size` overrides the
repository default.
