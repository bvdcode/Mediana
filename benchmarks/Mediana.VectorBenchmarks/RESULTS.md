# SVG and TGS rendering results

Baseline `43bf6feb`; drawing changes `6b24a31a` and `4ce5360b`. Linux x64, .NET 11.0.0, Release, Intel Core i5-12450H. Both versions used CPU affinity 0–7, `DOTNET_PROCESSOR_COUNT=4`, disabled tiered compilation and 2× supersampling. The WebP and VP8 encoder assemblies were identical.

[Measurements from every process](results.json). The main matrix contains 25 cases, three fresh processes per version and case, with alternating version order. Values below are medians of those three process measurements. SVG output is 200 × 120; TGS output is 200 × 200.

| 200 px workload | Elapsed ms before → after | CPU ms/image before → after | Managed KiB/image before → after | Peak process MiB before → after |
| --- | ---: | ---: | ---: | ---: |
| SVG effects, render | 41.03 → 12.11 | 54.32 → 17.35 | 1067.2 → 1055.3 | 77.1 → 77.1 |
| Simple TGS, render | 1.70 → 1.63 | 1.75 → 1.68 | 591.8 → 591.8 | 66.3 → 66.3 |
| TGS effects, render | 64.66 → 13.80 | 68.79 → 15.61 | 376.8 → 365.6 | 63.9 → 67.7 |
| SVG effects, read + render + WebP | 41.48 → 30.84 | 54.81 → 26.58 | 3105.9 → 3003.1 | 79.9 → 79.9 |
| Simple TGS, read + render + WebP | 2.93 → 2.81 | 3.01 → 2.98 | 1965.2 → 1964.9 | 63.7 → 63.9 |
| TGS effects, read + render + WebP | 66.49 → 14.79 | 70.61 → 16.52 | 1748.4 → 1737.2 | 81.9 → 81.9 |

SVG timings varied between processes: the updated complete-preview medians ranged from 12.88 to 31.47 ms. An additional 100-preview sequential run on the final build measured 39.43 → 13.90 ms, CPU 51.25 → 18.59 ms/image, and identical RGBA/WebP. This additional pair is included separately in the result file; it does not replace the original three-process matrix.

Four concurrent rendering contexts at 200 px:

| Workload | Batch latency ms before → after | CPU ms/image before → after | Peak process MiB before → after |
| --- | ---: | ---: | ---: |
| SVG effects | 67.00 → 19.13 | 67.80 → 19.29 | 96.9 → 104.1 |
| Simple TGS | 3.00 → 2.84 | 2.50 → 2.32 | 81.8 → 80.8 |
| TGS effects | 102.73 → 23.06 | 86.94 → 19.45 | 81.7 → 80.1 |

Other sizes and scenes:

- At 64 px, effect-heavy SVG and TGS rendering improved 3.20× and 3.97×.
- At 640 px, TGS effects improved 4.93×; translucent trimmed groups improved 4.26×. The SVG effects case had substantial elapsed-time variation; its median CPU time fell from 535.63 to 218.72 ms/image.
- The larger rotated-blur scene improved only 1.20× elapsed and 1.06× CPU; its convolution cost remains a separate optimization opportunity.
- Geometry-only TGS and the simple orbit scene remained approximately unchanged, including 2% slower medians in two cases. The improvements primarily affect filtering and compositing.
- Cumulative allocations generally declined slightly. Peak process memory was mostly comparable, with a 7.2 MiB increase in the concurrent SVG case. These changes do not establish a general reduction in peak memory.

## Changes

Channel-independent color matrices and component transfers use exact byte lookup tables, including cross-channel mappings and linear/sRGB conversion. General matrices use vector arithmetic. Filter loops reuse row spans; alpha extraction copies the alpha channel directly. Shifted effects sample only the region that can contain nonzero premultiplied pixels. Morphology skips empty lines and transparent output ranges. Shadow tinting reuses its offset buffer.

Integer-translated images and ordinary isolated layers composite directly over clipped rows. Non-isolated layers, knockout groups and tracked shapes retain their existing compositing path. No supersampling, blur radius, color function or effect was removed or approximated.

## Validation

- All 25 benchmark cases produced identical RGBA bytes before and after, across every repetition and concurrent context. All measured WebP outputs were also identical.
- 498 managed tests passed across drawing, SVG, TGS, PDF, Word and presentation rendering.
- 18 focused tests passed with hardware intrinsics disabled.
- The Linux x64 Native AOT executable passed channel-matrix/transfer, cancellation, translated-image, strided-frame, SVG reference and 33-frame TGS reference checks. Native compilation treated warnings as errors.
- License-header verification passed for the repository.

These are comparisons between two Mediana builds. Skia was not measured in this run; parity with its rendering time and output quality is not established by these results.
