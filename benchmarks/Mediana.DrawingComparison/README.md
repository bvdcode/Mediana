# Drawing comparison

Standalone comparison of Mediana.Drawing with ImageSharp 4.1.3, ImageSharp.Drawing 3.1.3 and Fonts 3.2.0, resolved through EasyExtensions.Drawing 3.0.95. The reference dependency belongs to this executable only.

The [6 November 2026 results](results/2026-11-06/README.md) contain all 111 workloads,
618 process measurements and 84 quality checks, including missed speed and memory targets.

Both engines receive the same geometry, font file, font size, baseline and text. Antialiasing runs at the requested canvas resolution. Font hinting and the reference text contrast boost are disabled to compare fractional area coverage. Full text layout uses each engine's kerning and measurement; differences in shaping and truncation require separate quality inspection.

Each process loads one font, records its first rendering, warms up, then measures a batch series. Every image owns a separate drawing context. Parallel runs share the parsed font and dispose each result. ImageSharp records the entire image in one drawing canvas and flushes on disposal. The reference's allocator and internal parallelism retain their defaults.

Reported metrics include elapsed and process CPU time per image, all-thread managed allocated bytes, GC counts, process peak RSS and final RSS. Managed allocations exclude native storage and reused pool capacity; peak RSS includes both. Peak RSS is cumulative from process startup and includes cold and warmup rendering. Optional raw RGBA export runs after the measurement and memory sample. Encoding and file I/O are excluded from rendering time.

Build and run:

```sh
dotnet build benchmarks/Mediana.DrawingComparison -c Release -o artifacts/drawing-comparison/current
DOTNET_PROCESSOR_COUNT=2 DOTNET_TieredCompilation=0 dotnet artifacts/drawing-comparison/current/Mediana.DrawingComparison.dll mediana dense-text 2560 2560 1 '<font-file>' 10 5 1 -
DOTNET_PROCESSOR_COUNT=2 DOTNET_TieredCompilation=0 dotnet artifacts/drawing-comparison/current/Mediana.DrawingComparison.dll imagesharp dense-text 2560 2560 1 '<font-file>' 10 5 1 -
```

Arguments: engine, scene, width, height, complexity, font file, minimum iterations, minimum measured seconds, concurrent images, export directory (`-` disables export), optional integer drawing scale (default 1). Scenes: `short-text`, `dense-text`, `mixed-text`, `table-empty`, `table-full`, `bars`, `curves`, `strokes`, `alpha`, `clipped-curves`, `transformed-curves`.

`run_comparison.py` runs `fonts`, `vectors`, `series`, `large` or `calibration` suites. Pass explicit `--dotnet`, `--binary`, `--font-directory`, `--output` and `--suite` arguments. `--repeats` defaults to 3; `--seconds` defaults to 3. Series workloads use 1, 2 and 4 concurrent contexts with matching processor allowances. Use 30 or more seconds for sustained workloads. The large suite renders a 2560px scene at scale 4 (10,240px canvas). Results are JSON lines; existing files are preserved.

Run paired processes sequentially, alternate engine order, repeat independently, and keep competing build/test work idle. Compare cold calls separately from sustained throughput. A speed result alone does not establish equal output quality or satisfaction of the performance target.

The Mediana target uses `FillPathAntialiased`, `StrokePathAntialiased` and `TextRenderer.DrawGlyphAntialiased`. These methods compute coverage at the output resolution, avoiding a larger supersampled canvas. Glyph placement uses a 1/256px grid. Share the immutable `FontProgram` between independent renderers; each `TextRenderer` owns its bounded geometry and coverage caches. Existing callers select the new antialiased APIs explicitly.

Each runner writes a metadata sidecar containing the measured assembly hashes, source revision, reference package versions, runtime, CPU model and CPU quota. Raw measurements retain all repetitions, cold calls, observed concurrency, total managed allocation and allocation per image. `summarize_results.py --output <report-prefix> <measurements.jsonl> ...` writes every workload and its independent-process range; the time ratio is Mediana divided by the reference. Values below 1 are faster.

Image quality is checked separately from timed runs:

```sh
python check_quality.py --dotnet '<dotnet>' --binary '<comparison.dll>' --font-directory '<font-directory>' --output artifacts/drawing-comparison/quality-fonts --suite fonts
python check_quality.py --dotnet '<dotnet>' --binary '<comparison.dll>' --font-directory '<font-directory>' --output artifacts/drawing-comparison/quality-vectors --suite vectors
```

This script requires Pillow. It renders the same scene natively in both engines and at 8x resolution for 200px scenes, or 4x for larger scenes, in the reference engine. It downsamples premultiplied RGBA with a box filter. The quality gate requires edge RGB RMSE no more than one byte code above the reference's native RMSE against that shared image. Alpha RMSE and direct native-image differences are recorded separately. Full-size PNGs and comparison strips support visual inspection; strips show Mediana, reference and downsampled reference from left to right. Table borders are drawn once per cell, including shared edges, in both engines.

After the timed loop and memory sample, parallel workloads render two additional images per worker and compare their complete SHA-256 pixel hashes with a single-context image. Any mismatch fails the process. `VerifiedConcurrentImages` records the checked count; these verification buffers and hashing are excluded from all reported performance metrics.
