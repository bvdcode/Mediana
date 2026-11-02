The H.264 forward quantizer now processes complete coefficient blocks with SSE2
on supported processors. Implementation `13ecd6d` is compared with `51ec1a1`,
including the preceding [direct chroma reference access](h264-search-work.md).
The existing scalar calculation remains in use on other processors.

## Computation

The signed quantization equation, scale factors, rounding and 15/16-coefficient
frame scan remain unchanged. The vector calculation forms unsigned 32-by-32-bit
products with 64-bit results through
[Sse2.Multiply](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.intrinsics.x86.sse2.multiply).
Narrow 32-bit multiplication would overflow for supported large coefficients.
Sign restoration occurs after the rounded shift. The minimum signed integer
still raises an overflow exception when selected; a chroma scan continues to
ignore its DC coefficient.

The four coefficient rows are reordered directly into frame scan order in
registers. The 15-coefficient path writes exactly its selected samples without
writing through the destination boundary. Native x64 disassembly confirms
PMULUDQ, integer shifts and register shuffles, with the scan helper inlined.
There is no intermediate coefficient array or additional heap allocation.

A preceding diagnostic on all 1,441 pictures counted 121,470,168 forward
quantization calls and 10,122,514 chroma DC calls. Instrumented elapsed times
were 12.491 seconds for quantization, 0.365 for chroma DC and 65.953 for complete
picture encoding. Timers add overhead; these nested values are not CPU
percentages. Chroma DC computation is unchanged.

## Complete encoding comparison

Each process reads the same 1,992,038,400-byte raw YUV input and writes the
complete MPEG-TS result: 1,441 pictures, 60.041667 seconds, 1280x720, 24 fps.
Configuration and source identities are those of the
[stage comparison](h264-stage-performance.md#input-and-measurement).
Frozen Native AOT binaries run serially on one assigned CPU, with one processing
thread and no overlapping build, validation or media workload. CPU is user plus
system time. These observations were collected on a shared Linux x64 host.

| Execution order | CPU seconds | Elapsed seconds | Peak RSS, KiB |
| --- | ---: | ---: | ---: |
| before-1 | 60.177137 | 64.253109 | 52,856 |
| after-1 | 48.612645 | 51.097034 | 50,956 |
| after-2 | 48.726223 | 49.183191 | 50,460 |
| before-2 | 59.684329 | 60.494292 | 52,668 |

Paired CPU reductions are **19.22%** and **18.36%**. Elapsed time and
peak RSS decrease in both pairs. The quantizer adds no owned media storage;
these process observations do not establish a general memory reduction.
All four outputs have SHA256
`d3090ee699cfdcc4330ec9815adaa1e7b08de1d95530e2e7af2e1596a399e543`.
Every process reaches EOF with zero owned media bytes. The complete encoded
bytes, size, timing and independently verified decoded quality are unchanged.

Fresh optimized FFmpeg/libx264 processes use the original single-thread
`veryfast` configuration and retain the independently decoded FFmpeg reference:

| Observation | CPU seconds | Elapsed seconds | Peak RSS, KiB |
| --- | ---: | ---: | ---: |
| 1 | 14.187575 | 14.189806 | 116,416 |
| 2 | 13.533126 | 13.548403 | 112,860 |

The ratio of CPU medians is **3.51x** (48.669 versus 13.860 seconds).
This is not equal-quality throughput: the FFmpeg reference remains smaller with
higher PSNR. The change concerns encoding; it establishes no new decoding or
complete-film audiovisual HLS throughput result.

## Validation

Release build, all 2,956 managed tests, 695 H.264 checks with hardware intrinsics
disabled, all 30 package checks, strict package-only Native AOT execution, the
native CLI build, formatting and root license-header verification passed.
Shared managed/native cases compare independent scalar equations at every QP,
signed rounding boundaries, mixed full-range coefficients, all minimum-integer
positions, skipped DC, source/output guards and repeated allocation-free calls.
Existing independent H.264, encoding, temporal and HLS references are unchanged.
