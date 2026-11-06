# Drawing comparison

Standalone comparison of Mediana.Drawing with ImageSharp 4.1.3, ImageSharp.Drawing 3.1.3 and Fonts 3.2.0, resolved through EasyExtensions.Drawing 3.0.95. The reference dependency belongs to this executable only.

Both engines receive the same geometry, font file, font size, baseline and text. Antialiasing runs at the requested canvas resolution. Font hinting and the reference text contrast boost are disabled to compare fractional area coverage. Full text layout uses each engine's kerning and measurement; differences in shaping and truncation require separate quality inspection.

Each process loads one font, records its first rendering, warms up, then measures a batch series. Every image owns a separate drawing context. Parallel runs share the parsed font and dispose each result. ImageSharp records the entire image in one drawing canvas and flushes on disposal. The reference's allocator and internal parallelism retain their defaults.

Reported metrics include elapsed and process CPU time per image, all-thread managed allocated bytes, GC counts, process peak RSS and final RSS. Managed allocations exclude native storage and reused pool capacity; peak RSS includes both. Peak RSS is cumulative from process startup and includes cold and warmup rendering. Optional raw RGBA export runs after the measurement and memory sample. Encoding and file I/O are excluded from rendering time.

Build and run:

```sh
dotnet build benchmarks/Mediana.DrawingComparison -c Release -o artifacts/drawing-comparison/current
DOTNET_PROCESSOR_COUNT=2 DOTNET_TieredCompilation=0 dotnet artifacts/drawing-comparison/current/Mediana.DrawingComparison.dll mediana dense-text 2560 2560 1 '<font-file>' 10 5 1 -
DOTNET_PROCESSOR_COUNT=2 DOTNET_TieredCompilation=0 dotnet artifacts/drawing-comparison/current/Mediana.DrawingComparison.dll imagesharp dense-text 2560 2560 1 '<font-file>' 10 5 1 -
```

Arguments: engine, scene, width, height, complexity, font file, minimum iterations, minimum measured seconds, concurrent images, export directory (`-` disables export). Scenes: `short-text`, `dense-text`, `mixed-text`, `table-empty`, `table-full`, `bars`, `curves`, `strokes`, `alpha`.

Run paired processes sequentially, alternate engine order, repeat independently, and keep competing build/test work idle. Compare cold calls separately from sustained throughput. A speed result alone does not establish equal output quality or satisfaction of the performance target.
