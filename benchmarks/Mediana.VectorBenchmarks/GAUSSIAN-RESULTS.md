# Gaussian rendering performance

Baseline `119c1bce`; changes `65090cde` and `ad5a385b`. Linux x64, .NET 11.0.0, Release, Intel Core i5-12450H. Both versions used CPU affinity 0–7 and `DOTNET_PROCESSOR_COUNT=4`. JIT tiered compilation was disabled. Native AOT used the default portable instruction-set target and `OptimizationPreference=Speed`.

[Measurements from every process](gaussian-results.json). The managed matrix contains 39 cases and 234 processes; Native AOT adds three cases and 18 processes. Each case has three fresh processes per version, with alternating version order. Tables contain the median of each metric across those three processes, before → after. Rendering, downsampling, disposal and RGBA verification are measured. See [measurement boundaries](README.md#measurement-boundaries) for warmup, allocation and peak-memory definitions. The host also runs other workloads; small timing differences do not establish a general speedup.

## JIT rendering

All rows below use 2× supersampling except the explicitly marked 2560 px rows, which use 1× in both versions. A 1280 px render at 2× uses a 2560 px internal canvas. SVG output preserves intrinsic proportions.

| Workload | Elapsed ms/image | CPU ms/image | Allocated MiB/image | Peak process MiB |
| --- | ---: | ---: | ---: | ---: |
| TGS, strong blur, 640 px | 485.48 → 82.22 | 1831.37 → 248.51 | 1.04 → 0.95 | 91.15 → 91.11 |
| TGS, strong blur, 1280 px | 3831.58 → 514.17 | 14168.57 → 1771.56 | 3.68 → 1.31 | 176.12 → 173.85 |
| SVG, rotated anisotropic blur, 200 px | 52.43 → 22.92 | 257.09 → 78.09 | 0.17 → 0.27 | 59.14 → 63.84 |
| SVG, rotated anisotropic blur, 640 px | 1590.16 → 648.32 | 6974.55 → 2453.53 | 1.25 → 1.81 | 79.49 → 83.46 |
| SVG, mixed effects, 1280 px | 562.47 → 495.94 | 914.40 → 698.63 | 17.08 → 17.08 | 186.60 → 186.54 |
| SVG, mixed effects, 2560 px at 1× | 521.60 → 486.01 | 820.20 → 686.99 | 17.08 → 17.08 | 193.61 → 193.82 |
| TGS, mixed effects, 1280 px | 620.27 → 619.55 | 746.51 → 724.65 | 0.88 → 0.88 | 249.30 → 249.50 |
| TGS, mixed effects, 2560 px at 1× | 570.93 → 569.29 | 693.44 → 674.58 | 0.88 → 0.88 | 262.62 → 262.61 |
| SVG, text and vectors, 1280 px | 250.50 → 252.71 | 250.50 → 252.72 | 16.02 → 16.02 | 169.15 → 169.12 |
| TGS, simple geometry, 1280 px | 64.41 → 62.41 | 64.43 → 62.39 | 7.31 → 7.31 | 144.44 → 144.56 |

The largest gains are in Gaussian convolution. Scenes dominated by geometry, text, compositing or other effects remain approximately unchanged. Mixed-effects SVG benefits more at larger sizes; mixed-effects TGS has a smaller improvement. These results do not establish a uniform acceleration of every effect.

## Native AOT rendering

All three cases use 640 px output and 2× supersampling. Each version was compiled independently with the same portable target.

| Workload | Elapsed ms/image | CPU ms/image | Allocated MiB/image | Peak process MiB |
| --- | ---: | ---: | ---: | ---: |
| TGS, strong blur | 501.23 → 119.04 | 1893.66 → 399.58 | 1.06 → 0.95 | 49.43 → 50.61 |
| SVG, rotated anisotropic blur | 1532.91 → 374.41 | 7533.77 → 1417.75 | 1.25 → 1.65 | 43.43 → 52.29 |
| SVG, mixed effects | 122.27 → 117.85 | 168.22 → 157.23 | 5.00 → 5.00 | 73.99 → 78.12 |

The convolution uses `System.Numerics.Vector<float>` and follows the vector width available to the compiler. It does not require an AVX2-only executable. Native AOT selects its baseline instruction set and vector width during publication; see the [.NET 11 Native AOT code-generation documentation](https://github.com/dotnet/runtime/blob/v11.0.0/src/coreclr/nativeaot/docs/optimizing.md).

## Concurrent rendering

Two independent contexts at 1280 px, plus four contexts for transformed blur at 200 px, each at 2× supersampling. Elapsed and CPU values below are divided by the number of completed images; per-batch median latency is also available in the JSON data.

| Workload | Elapsed ms/image | CPU ms/image | Allocated MiB/image | Peak process MiB |
| --- | ---: | ---: | ---: | ---: |
| SVG mixed effects, two contexts | 317.48 → 290.64 | 962.13 → 804.92 | 16.98 → 17.04 | 294.36 → 294.31 |
| TGS mixed effects, two contexts | 392.47 → 336.45 | 857.70 → 757.43 | 0.85 → 0.84 | 414.81 → 414.81 |
| SVG rotated blur, four contexts | 46.87 → 18.17 | 265.58 → 95.81 | 0.21 → 0.27 | 72.94 → 79.20 |

## Implementation and memory

Aligned Gaussian passes accumulate adjacent RGBA pixels in four independent SIMD vectors. The kernel weights and each pixel's accumulation order are unchanged. The SIMD results occupy a small stack buffer; the existing per-worker sample row is reused.

Transformed Gaussian passes iterate weights across an output row. Each worker caches two premultiplied source rows and one accumulation row, instead of converting four RGBA samples again for every pixel and weight. Transparent margins are excluded from cached source columns when the edge mode is `none`. The worker count and scratch storage are bounded; scratch is charged to `MaximumEffectBytes` before allocation. Full floating-point images are not allocated.

The transformed path trades additional row storage for lower CPU use. Its cumulative allocations and process peak increase in the reported cases; this is not a general memory-reduction result. Cancellation checks remain inside convolution loops. Default output quality, supersampling, Gaussian radius, interpolation, alpha handling and color conversion remain unchanged.

## Validation

- All 39 managed cases and three Native AOT cases matched every RGBA byte between versions, including repeated and concurrent calls. All measured WebP outputs also matched byte-for-byte.
- 505 managed tests passed across drawing, SVG, TGS and document rendering.
- 23 focused tests passed with AVX2 disabled, and 25 passed with hardware intrinsics disabled.
- The scalar Gaussian reference covers 936 combinations: four image sizes, three alpha distributions, thirteen directions/deviations, three edge modes and two color spaces. Cases include tiny images, transparent RGB, kernels wider than the image, negative directions and deviations near the supported maximum.
- Budget exhaustion, reservation release and cancellation passed.
- Native AOT compilation treated warnings as errors. Its executable passed drawing/filter tests, the SVG reference corpus and 33 TGS reference frames.
- Repository license-header validation passed.
