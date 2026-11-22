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
  tests/Mediana.Tests/Data artifacts/vector/results
```

The result directory must be empty. The script starts three fresh processes per version and case, reverses version order on the second repetition, and compares every exported RGBA and WebP byte. Run without competing CPU workloads. An external CPU affinity setting can be applied to the script and will be inherited by both versions.

Individual case:

```sh
dotnet artifacts/vector/after/Mediana.VectorBenchmarks.dll \
  tests/Mediana.Tests/Data/Tgs/effects.tgs 200 2 .5 40 1 preview artifacts/vector/sample
```

Arguments: input, output size, supersampling factor, normalized animation position, measured rounds, concurrent render contexts, mode, export directory. SVG fits its intrinsic proportions inside the requested square; TGS renders to that square. Position is in `[0, 1)` and is ignored for SVG.

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

[Measured changes, memory and validation](RESULTS.md).
