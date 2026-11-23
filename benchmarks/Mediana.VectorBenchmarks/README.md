# SVG and TGS benchmarks

Measures SVG and Lottie/TGS previews with identical supersampling and pixel output. Each process reports first-call latency, warmed batch latency, throughput, whole-process CPU time, managed allocation volume, collection counts and peak working set.

## Run

Keep all baseline Mediana assemblies in one directory. Build the same harness against each version:

```sh
dotnet build benchmarks/Mediana.VectorBenchmarks -c Release -o artifacts/vector/after
dotnet build benchmarks/Mediana.VectorBenchmarks -c Release \
  -p:VectorReferenceDirectory="<absolute baseline assembly directory>" \
  -o artifacts/vector/before
bash benchmarks/Mediana.VectorBenchmarks/run-comparison.sh \
  artifacts/vector/before artifacts/vector/after \
  tests/Mediana.Tests/Data artifacts/vector/results all
```

The optional suite is `standard` (the original 25 cases, default), `large` (14 additional cases) or `all` (39 cases). The large suite adds 1280 px output at 2× supersampling, 2560 px output at 1×, two concurrent large renders and transformed anisotropic blur. Each before/after pair uses identical settings.

The result directory must be empty. The script starts three fresh processes per version and case, reverses version order on the second repetition, and compares every exported RGBA and WebP byte. Run without competing CPU workloads. An external CPU affinity setting can be applied to the script and will be inherited by both versions.

Individual case:

```sh
dotnet artifacts/vector/after/Mediana.VectorBenchmarks.dll \
  tests/Mediana.Tests/Data/Tgs/effects.tgs 200 2 .5 40 1 preview artifacts/vector/sample
```

Arguments: input, output size, supersampling factor, normalized animation position, measured rounds, concurrent render contexts, mode, export directory. SVG fits its intrinsic proportions inside the requested square; TGS renders to that square. Position is in `[0, 1)` and is ignored for SVG.

## Native AOT comparison

Publish the same harness against each saved set of assemblies. Use the default instruction-set target for portable binaries:

```sh
dotnet publish benchmarks/Mediana.VectorBenchmarks -c Release -r linux-x64 \
  -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true \
  -p:VectorReferenceDirectory="<absolute baseline assembly directory>" \
  -o artifacts/vector/before-native
```

Repeat with the updated assembly directory and `after-native` output. Invoke each executable with the same eight arguments as the managed harness, for example:

```sh
artifacts/vector/after-native/Mediana.VectorBenchmarks \
  tests/Mediana.Tests/Data/Tgs/trimmed-groups-blur.tgs \
  640 2 .75 6 1 render artifacts/vector/native-sample
```

Repeat paired measurements in fresh processes, alternate version order, and compare exported RGBA bytes. JIT and Native AOT are separate comparisons; their absolute times should not be combined.

## Measurement boundaries

- `render` shares a parsed document and measures rendering, supersampling reduction and disposal. Each call owns its drawing context and pixel buffers. SVG scene parsing is excluded; TGS evaluation and scene construction are part of rendering.
- `preview` reads the in-memory encoded document again, renders it and encodes lossless WebP. File reads, reference exports and JSON serialization are excluded. Both versions must use the same WebP encoder binaries to isolate renderer changes.
- Every measured image is checked against the initial RGBA output. Preview mode also checks the encoded WebP hash. CPU and elapsed totals include these checks and concurrent task scheduling.
- First-call latency is the first operation after document initialization; it includes remaining JIT and lazy initialization costs. It is not process startup or first file-read latency.
- Warmup runs for at least 500 ms and three batches. Timed rounds start after a full collection. `MedianBatchMilliseconds` is batch latency; elapsed and CPU per image divide totals by completed images. Four concurrent contexts are four independent operations, each of which may also use parallel filter kernels.
- Rendering uses the public default pooled allocator. Managed allocations include all worker threads. Peak working set is the process high-water mark over initialization, warmup and measurement, including runtime and pooled buffers; it is distinct from live pixel storage and cumulative allocations.
- The script sets `DOTNET_PROCESSOR_COUNT=4` and disables tiered compilation for both versions. It covers 64, 200 and 640 px; SVG effects and text; simple and effect-heavy TGS; multiple animation positions; blur, masks, gradients, trimmed strokes and translucent groups.

The fixture files are already included in the test corpus. The effects SVG, orbit TGS and effects TGS match the corresponding preview fixtures byte-for-byte.

## Results

[Initial filter and compositing changes](RESULTS.md).

[Gaussian SIMD and row-sampling changes, including large rendering](GAUSSIAN-RESULTS.md).

## Render settings

`DrawingRenderOptions.Width` and `Height` select the output size before rasterization. Set one dimension to preserve intrinsic proportions, or both to request a specific viewport. An SVG or TGS document can be parsed once and rendered repeatedly; each rendering call owns its temporary buffers.

`Supersampling` defaults to 2 and accepts 1–4. The internal canvas has the output width and height multiplied by this factor. Reducing it is a quality tradeoff, especially for thin lines, text and curves. The renderer does not lower it automatically. The Gaussian optimizations retain the same kernel weights, accumulation order, interpolation, alpha arithmetic and color conversion.

`MaximumSamples` and `MaximumEffectBytes` are resource budgets, not speed controls. When rendering large documents, configure them for the requested internal canvas and concurrent rendering load; exceeding a budget produces an error. `AllowPartialRendering` permits unsupported features and is not a performance setting.
