# Drawing benchmarks

Compares drawing, integer downsampling and lossless WebP encoding separately. The matrix covers short and dense text, Latin and Cyrillic, two font families, 6 × 14 tables, repeated width measurement during truncation, rounded bars, compound curves, translucent tiles and antialiased strokes. Output sizes range from 200 × 200 to 2560 × 2560; supersampling reaches a 10,240 × 10,240 canvas.

The baseline is Mediana at `4ec1688`. Both versions run the same benchmark source and WebP encoder. No external drawing implementation is involved in these measurements.

## Run

Keep the baseline `Mediana.Drawing`, `Mediana.Core`, `Mediana.Codecs` and `Mediana.Codecs.Webp` binaries together. Build the harness twice: once with current project references, once against those baseline binaries.

```sh
dotnet build benchmarks/Mediana.DrawingBenchmarks -c Release -o artifacts/drawing/after
dotnet build benchmarks/Mediana.DrawingBenchmarks -c Release \
  -p:DrawingReferenceDirectory="<absolute baseline binary directory>" \
  -o artifacts/drawing/before
bash benchmarks/Mediana.DrawingBenchmarks/run-comparison.sh dotnet \
  artifacts/drawing/before artifacts/drawing/after artifacts/drawing/results
```

Use an empty results directory. The script runs 23 individual workloads and nine sequential/concurrent series, with three fresh processes per version and case. It reverses version order on the second repetition. `DOTNET_TieredCompilation=0` removes tier transitions; `DOTNET_PROCESSOR_COUNT` is two for individual workloads and four for concurrent series. No build or test runs accompany the measurements.

For a single workload:

```sh
dotnet artifacts/drawing/after/Mediana.DrawingBenchmarks.dll text 2560 2560 4 0 artifacts/drawing/text
dotnet artifacts/drawing/after/Mediana.DrawingBenchmarks.dll batch text 2560 4 4 3
```

The individual workload arguments are scene, output width, output height, supersampling factor, measured iterations (`0` selects automatically), and export directory. Batch arguments are scene, square output size, supersampling factor, maximum concurrent contexts and rounds.

## Measurements

- Individual workloads warm up for at least 500 ms and three calls. Rendering includes canvas allocation, initialization and a new drawing/font context. The immutable parsed font is shared across calls. Downsampling includes its output allocation. Encoding uses zero-copy frame publication. Raw pixel exports and image hashing are outside the measured stages.
- CPU is whole-process processor time across the measured iterations, divided by the image count. Stage times are elapsed time. The encoder is included in individual CPU totals.
- Batch series exclude WebP and include drawing, downsampling and pixel hashing. They use the same parsed font across independent contexts, report first-call drawing separately, and verify every image against the initial pixel hash. First-call time includes JIT compilation but excludes font parsing. `ObservedParallelism` records actual overlapping calls.
- Allocations are managed bytes allocated per image, including pixel arrays. Batch measurements count allocations across all worker threads and record collection counts for generations 0, 1 and 2. They include scheduling and hash overhead.
- Peak working set is the OS process high-water mark over initialization, warmup, measurement and exports. It is not live pixel storage or total allocated bytes. A 10,240 × 10,240 RGBA canvas alone occupies 400 MiB; its 2560 × 2560 output adds 25 MiB.

The accompanying results contain each process measurement. Summaries use medians of the three processes. CPU and memory figures apply to the documented workload and runtime.

## Results

Linux x64, .NET 10.0.14, Release. [All 192 process measurements](results.json). The four dependent baseline binaries were verified byte-for-byte against the original build before running the updated harness.

All 23 individual workloads produced identical RGBA and WebP files before and after. Pixel hashes matched across every repetition and concurrent context. Every series reached its requested concurrency of one, two or four.

| Workload | Output | Scale | Draw ms | Downsample ms | CPU/image ms, including WebP | Draw allocation MiB |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Dense mixed text | 200 × 200 | 1× | 1.01 → 0.95 | 0.00 → 0.00 | 1.94 → 1.92 | 1.46 → 1.34 |
| Dense mixed text | 2560 × 2560 | 1× | 66.44 → 38.81 | 0.00 → 0.00 | 126.96 → 102.31 | 66.93 → 55.76 |
| Dense mixed text | 2560 × 2560 | 2× | 227.56 → 75.71 | 221.67 → 69.20 | 550.86 → 231.31 | 163.91 → 139.34 |
| Dense mixed text | 2560 × 2560 | 4× | 808.80 → 167.25 | 647.61 → 162.24 | 1541.17 → 404.67 | 498.50 → 443.78 |
| Short mixed text | 200 × 200 | 1× | 0.17 → 0.14 | 0.00 → 0.00 | 0.59 → 0.56 | 0.35 → 0.35 |
| Short mixed text | 2560 × 2560 | 4× | 90.95 → 58.22 | 643.25 → 180.77 | 757.59 → 264.11 | 400.46 → 400.26 |
| Dense Latin text | 200 × 200 | 1× | 0.98 → 0.90 | 0.00 → 0.00 | 1.91 → 1.83 | 1.51 → 1.38 |
| Dense Latin text | 2560 × 2560 | 4× | 833.64 → 189.19 | 650.14 → 175.33 | 1570.49 → 461.33 | 519.10 → 462.87 |
| Mostly empty table | 200 × 200 | 2× | 7.53 → 3.71 | 1.45 → 0.59 | 9.94 → 5.24 | 2.11 → 1.86 |
| Mostly empty table | 2560 × 2560 | 2× | 65.10 → 26.15 | 213.66 → 71.09 | 319.17 → 144.89 | 105.97 → 101.77 |
| Filled table | 200 × 200 | 2× | 29.07 → 13.62 | 1.35 → 0.53 | 31.74 → 15.34 | 3.95 → 3.61 |
| Filled table | 2560 × 2560 | 2× | 150.07 → 43.34 | 219.19 → 70.16 | 419.64 → 161.53 | 114.24 → 105.30 |
| Repeated width measurement | 200 × 200 | 1× | 25.17 → 11.29 | 0.00 → 0.00 | 25.20 → 11.32 | 0.18 → 0.18 |
| Repeated width measurement | 2560 × 2560 | 1× | 25.31 → 11.97 | 0.00 → 0.00 | 28.11 → 14.78 | 25.03 → 25.03 |
| Rounded bars | 200 × 200 | 2× | 2.87 → 0.36 | 1.31 → 0.40 | 4.90 → 1.42 | 1.21 → 0.71 |
| Rounded bars | 640 × 480 | 2× | 20.29 → 1.56 | 8.62 → 3.21 | 31.73 → 7.42 | 6.02 → 4.84 |
| Rounded bars | 2560 × 2560 | 2× | 404.06 → 16.72 | 194.64 → 58.16 | 641.34 → 124.08 | 106.60 → 100.25 |
| Compound curves | 200 × 200 | 2× | 3.71 → 1.42 | 1.15 → 0.35 | 5.62 → 2.46 | 1.14 → 1.18 |
| Compound curves | 1024 × 768 | 1× | 13.32 → 3.90 | 0.00 → 0.00 | 19.15 → 9.86 | 3.57 → 3.57 |
| Antialiased strokes | 200 × 200 | 1× | 37.30 → 4.75 | 0.00 → 0.00 | 38.02 → 5.40 | 2.20 → 1.53 |
| Antialiased strokes | 1280 × 960 | 1× | 156.67 → 13.61 | 0.00 → 0.00 | 166.30 → 23.34 | 8.93 → 7.36 |
| Translucent tiles | 200 × 200 | 2× | 5.82 → 1.33 | 1.15 → 0.40 | 7.61 → 2.37 | 1.65 → 1.00 |
| Translucent tiles | 640 × 480 | 2× | 42.70 → 7.91 | 9.52 → 3.62 | 53.96 → 13.49 | 6.66 → 5.08 |

For dense mixed text on a 10,240 × 10,240 canvas, drawing allocation falls from 498.50 to 443.78 MiB. Beyond the 400 MiB canvas itself, this is 98.50 → 43.78 MiB. The small compound-curve workload allocates 1.14 → 1.18 MiB while drawing 2.6 times faster.

Concurrent series below use the same parsed font and separate drawing contexts. They exclude WebP. Elapsed time per image is batch elapsed time divided by completed images, so it describes throughput.

| Workload | Contexts | First draw + resize ms | Warm elapsed/image ms | Warm CPU/image ms | Allocated MiB/image | Peak process RSS MiB |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Filled table, 200 px, 2× | 1 | 81.44 → 66.18 | 30.11 → 14.55 | 30.11 → 14.56 | 4.10 → 3.76 | 58.16 → 59.49 |
| Filled table, 200 px, 2× | 2 | 83.13 → 66.89 | 15.71 → 7.66 | 30.44 → 14.95 | 4.10 → 3.76 | 59.36 → 62.54 |
| Filled table, 200 px, 2× | 4 | 82.42 → 67.89 | 10.33 → 5.85 | 36.46 → 18.02 | 4.10 → 3.76 | 59.77 → 62.63 |
| Dense mixed text, 2560 px, 4× | 1 | 1472.59 → 437.29 | 1485.20 → 360.46 | 1485.11 → 360.48 | 523.50 → 468.78 | 1751.88 → 1750.60 |
| Dense mixed text, 2560 px, 4× | 2 | 1497.27 → 424.67 | 791.60 → 192.50 | 1523.25 → 368.64 | 523.50 → 468.78 | 2608.30 → 2608.30 |
| Dense mixed text, 2560 px, 4× | 4 | 1488.78 → 422.88 | 489.11 → 124.88 | 1814.78 → 447.80 | 523.50 → 468.78 | 5161.04 → 5161.36 |
| Rounded bars, 2560 px, 2× | 1 | 751.23 → 136.45 | 673.51 → 106.48 | 674.87 → 107.37 | 131.60 → 125.25 | 574.78 → 628.39 |
| Rounded bars, 2560 px, 2× | 2 | 762.37 → 147.01 | 402.45 → 57.80 | 773.61 → 111.56 | 131.60 → 125.25 | 940.76 → 1106.64 |
| Rounded bars, 2560 px, 2× | 4 | 699.72 → 155.18 | 262.35 → 40.59 | 888.16 → 130.35 | 131.60 → 125.25 | 1506.69 → 1634.93 |

Peak process RSS remains approximately unchanged in the large-text series even though allocations per image fall. The rounded-bar series reduces CPU substantially; its peak RSS increases in these series. The result file includes all collection counts and first-call allocations.

Validation: 3783 managed tests passed across both test projects, 18 focused tests passed with hardware intrinsics disabled, and the Linux x64 Native AOT test executable passed. Coverage tests compare both fill rules against a full-edge scalar reference, including curved strokes, coincident crossings, transforms and clipped contours. Downsampling matches premultiplied-alpha reference arithmetic for factors 1, 2, 3, 4, 8 and 16. Normal compositing over opaque pixels matches the normalized-float reference for all 16,777,216 channel/alpha combinations.
