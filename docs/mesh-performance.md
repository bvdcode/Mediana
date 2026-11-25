# Mesh CPU, memory and output comparison

`Mediana.Models.Mesh` is compared with the original Render.NET Mesh source at
[`8d2a437`](https://github.com/bvdcode/Render.NET/tree/8d2a4375c719c9bc394f755a4c989ed685bd36b6).
The candidate includes single-pass bounded OPC XML parsing. Both implementations
produce exactly the same 512 × 512 straight-alpha RGBA bytes on all six models.
Native CLI PNGs, decoded independently with FFmpeg, also match those bytes.

## Workload and measurement

Each operation loads the complete model from a file, creates its complete build
where applicable, renders with default options, and copies the final RGBA bytes
for comparison. That final 1 MiB verification copy is identical in both measured
consumers. Normal library/CLI frame publication transfers storage without it.
PNG encoding is outside this timing; it is verified separately.

Both use a 512 × 512 canvas, 2 × 2 supersampling, 1024 × 1024 shadow map, default
camera/material, shadows and ambient occlusion. The same consumer compiles the
original source and references the Mediana modules, invoking one implementation
per process. Models are the corresponding tracked Render.NET fixtures at the
linked revision; their hashes are listed below.

Release .NET 10.0.14, SDK 10.0.403, Linux x64, kernel 6.8.0, Intel i5-12450H.
Every process is pinned to logical CPUs 2 and 4, which are two different
performance cores. `DOTNET_PROCESSOR_COUNT=2` and `DOTNET_TieredCompilation=0`
are identical for both implementations; row shading uses the default parallel
policy. These are optimized JIT measurements. Native AOT execution is verified
separately, without treating these numbers as Native AOT timings.

There are three fresh processes per implementation/model, with rotating order.
Each process performs one warm-up operation, a full collection, then three
measured operations. The tables show medians of the three process means, or
process lifetime peaks for RSS. CPU is total process user plus system time.
The host is shared; CPU ranges remain wide even with fixed affinity.
Complete observations are in [the measurement CSV](mesh-performance.csv).

## Complete read and render

| Model | Render.NET elapsed / CPU, ms | Mediana elapsed / CPU, ms | Render.NET / Mediana peak RSS, MiB | Render.NET / Mediana allocated, MiB |
| --- | ---: | ---: | ---: | ---: |
| benchy.stl | 485.2 / 604.0 | 482.9 / 607.6 | 212.54 / 161.43 | 130.66 / 111.09 |
| spot_triangulated.obj | 185.0 / 315.9 | 183.7 / 321.0 | 112.68 / 112.00 | 32.07 / 31.58 |
| spot_quadrangulated.obj | 185.9 / 324.1 | 224.6 / 394.2 | 112.41 / 111.72 | 30.84 / 30.28 |
| mug.3mf | 238.8 / 380.1 | 265.6 / 436.8 | 124.91 / 123.45 | 29.46 / 27.17 |
| mech.3mf | 254.8 / 320.0 | 258.8 / 326.5 | 149.34 / 145.21 | 83.05 / 65.37 |
| dog.3mf | 345.9 / 414.0 | 382.9 / 468.3 | 181.17 / 221.46 | 125.75 / 102.65 |

| Model | Render.NET process-mean CPU range, ms | Mediana process-mean CPU range, ms |
| --- | ---: | ---: |
| benchy.stl | 593.5–936.2 | 601.1–1003.9 |
| spot_triangulated.obj | 307.6–525.3 | 307.7–526.2 |
| spot_quadrangulated.obj | 305.3–484.7 | 305.6–419.4 |
| mug.3mf | 379.3–639.1 | 394.3–504.7 |
| mech.3mf | 318.2–442.4 | 319.1–408.9 |
| dog.3mf | 400.8–604.7 | 381.8–481.0 |

Mediana CPU medians are 0.6–21.6% higher in this series, with overlapping ranges
for every model. The observations do not establish a stable CPU advantage or a
precise architectural regression magnitude. Managed allocation is lower on every
model. Peak RSS is about 51 MiB lower on the large binary STL and 40 MiB higher
on `dog.3mf`. Lower allocation does not guarantee lower process memory: RSS
includes retained GC heap, code and runtime state. The larger 3MF process peak
remains an optimization target.

## Removing a repeated XML parse

The OPC reader previously parsed every XML part completely to validate limits,
then parsed it again to build the tree. The current reader checks cancellation,
depth and the package-wide element count while the tree loader consumes nodes.
DTD and external entity resolution remain prohibited. The same measurement
series includes the two-pass Mediana implementation as a separate control.

| Model | Two-pass / single-pass read elapsed, ms | Two-pass / single-pass complete CPU, ms |
| --- | ---: | ---: |
| mug.3mf | 15.520 / 13.320 | 391.726 / 436.829 |
| mech.3mf | 148.970 / 115.883 | 352.761 / 326.496 |
| dog.3mf | 292.610 / 244.139 | 493.568 / 468.262 |

Median model-read elapsed time falls by 14–22% on these three 3MF inputs.
Complete CPU improves on `mech.3mf` and `dog.3mf`, while `mug.3mf` is slower in
aggregate. Timing ranges and shared-host variation prevent claiming a universal
end-to-end speedup. Every control and candidate output retains exact RGBA equality.

## Inputs and expected output

RGBA hashes cover exactly 1,048,576 bytes per model. PNG compression bytes may
differ independently of pixel equality.

| Model | Input SHA-256 | RGBA SHA-256 |
| --- | --- | --- |
| benchy.stl | `6ab57f1c3f8e86bc3cbd302c6fa6270acf06277c6335454e922419c25d42e97e` | `6b939c8fcca3c0d354b9ec6bb64c87e27528308a03aaa069135e3e9766127ce0` |
| spot_triangulated.obj | `0738b5e8608fed74e5e8c7aa8dd0af97b4b74f9f6cbf7aac84cd7e40b2e44a75` | `277923a7d5770f54c9f52d3807e3ea600e5a05ce9a606fa7373f9f4d3172c6ec` |
| spot_quadrangulated.obj | `08bf90be56612f0d2643a75a8764c4ca8fac7fe983ab91b53b51d37981e5757c` | `b0f1b131d39bbf5bc9205f2d9965a287c373149567231a29fc48aafd90d3d13b` |
| mug.3mf | `fec5be6ba569503db242c650f903f88a027511bba2dcd13b08bca533cbcdf17d` | `83700b94ffed38c2c0fa154d7420b56f611425904fd08b746f0708a642bf1129` |
| mech.3mf | `3d540eebebf01b3dc64bc7b1f1685c21ba40345fc916e8a4fd9c3e2698ee2b61` | `bc9127f9196a9336c682c91d0576bec633919204ea38b4dda68ddd0b29ef123b` |
| dog.3mf | `6bf89d150866041766f3b7da3776470960ca63855ffdf968aa6f8978b7511f4b` | `57055202fc6bcefc7fdd22c0e19d117d7c893184a37702bf911c404a4178e565` |

ASCII STL and cross-part Production references have authored managed/Native AOT
correctness checks; this real-model timing corpus does not exercise those variants.
The catalog keeps their performance checks open. This series covers complete
geometry loading and a single preview, rather than model writing, animation,
textures, other resolutions or other target platforms.

## Repeated rendering during model rotation

This comparison loads each model once, then renders evenly spaced Z-axis
rotations. It measures the full `MeshRenderer.Render` call, including geometry
preparation and both depth passes. File loading, display and PNG encoding are
outside the measurement. Both versions use the same studio lighting, shadow map
and ambient occlusion. Spot is from
[Keenan Crane's 3D Model Repository](https://www.cs.cmu.edu/~kmcrane/Projects/ModelRepository/index.html#spot).

Stage timing for triangulated Spot at 384 × 384 with one sample per pixel
attributed about 63% of elapsed time to shading, which includes shadow filtering
and ambient occlusion. At 512 × 512 with four samples per pixel, shading accounted
for about 77%. The renderer now computes light coefficients and filter offsets
once per frame, and reuses the horizontal and vertical depth terms in each
5 × 5 shadow filter. It uses the same taps, weights and depth tests.

The baseline is `66f99794`. Measurements use .NET 11.0.0 on Linux x64,
Intel Core i5-12450H, two pinned performance cores and
`MaximumDegreeOfParallelism = 2`. JIT tiering is disabled. Each case alternates
the order of four independent process pairs, with 12 warm-up frames per
process. The shadow map is 512 × 512 at one sample per pixel and 1024 × 1024
at four. A byte-by-byte check across nine configurations and 168 frames per
runtime matched the baseline RGBA in both JIT and Native AOT.

Each table reports medians of per-process means. CPU time sums user and system
time for the process. The pooled cases reuse one `MediaBufferAllocator` backed by
`MemoryPool<byte>.Shared` throughout the rotation in both builds. Peak RSS is
for the whole process, including the model and any retained pool buffers.

| JIT workload | Elapsed ms/frame | CPU ms/frame | Peak RSS, MiB |
| --- | ---: | ---: | ---: |
| Spot triangles, 256 × 256, 1 sample, pooled | 27.11 → 20.86 | 25.85 → 20.85 | 49.9 → 50.0 |
| Spot triangles, 384 × 384, 1 sample | 41.58 → 33.16 | 46.10 → 36.59 | 71.0 → 61.1 |
| Spot triangles, 512 × 512, 4 samples | 255.71 → 167.00 | 252.47 → 166.25 | 124.6 → 122.3 |
| Spot quads, 384 × 384, 1 sample, pooled | 44.73 → 32.90 | 44.29 → 32.01 | 52.5 → 52.4 |
| Dog assembly, 384 × 384, 1 sample, pooled | 36.25 → 31.29 | 42.32 → 36.13 | 129.1 → 129.2 |

| Native AOT workload | Elapsed ms/frame | CPU ms/frame | Peak RSS, MiB |
| --- | ---: | ---: | ---: |
| Spot triangles, 256 × 256, 1 sample, pooled | 22.72 → 19.52 | 23.85 → 20.14 | 25.1 → 24.9 |
| Spot triangles, 384 × 384, 1 sample | 41.26 → 34.83 | 42.19 → 35.03 | 36.2 → 64.6 |
| Spot triangles, 512 × 512, 4 samples | 152.35 → 125.66 | 211.75 → 174.59 | 96.5 → 96.3 |
| Spot quads, 384 × 384, 1 sample, pooled | 23.14 → 19.57 | 36.30 → 29.56 | 27.3 → 27.3 |
| Dog assembly, 384 × 384, 1 sample, pooled | 30.56 → 28.80 | 37.13 → 33.75 | 97.5 → 97.6 |

The higher unpooled 384 × 384 Native AOT RSS did not recur in a second
four-pair series. The medians were 35.9 → 36.2 MiB, with about 4.80 MB of
managed allocation per frame in both builds. With pooling on the same input,
RSS was 27.8 → 27.7 MiB and allocation was 3.55 → 4.02 KB/frame. The change
adds fixed-size lighting and filter data per frame.

With both shadows and ambient occlusion disabled, the 384 × 384 pooled Spot
JIT case took 8.96 → 8.30 ms/frame elapsed and 11.50 → 10.54 ms/frame CPU. These
measurements cover rendering on the tested Linux host. Window presentation and
Windows performance are separate measurements.

## Depth rasterization and normal generation during rotation

This series starts at `6a1163fa`, which already includes the lighting changes
above. It compares the complete render call on the same host and two cores,
with four alternating process pairs per case and 12 warm-up frames per process.
The 384 × 384 cases share one allocator pool. The 512 × 512 case uses the
default unpooled allocation strategy. Full shading enables shadows and ambient
occlusion. The plain case disables both.

The renderer groups incident corners once per vertex position during normal
generation. During rasterization it shares the scanline terms of the triangle
edge equations across samples and tests four depths at once with `Vector128`.
Coverage, equal-depth ordering and normal interpolation use the same rules in
both paths. Each of nine configurations matched the baseline RGBA byte for byte
over a total of 168 rotated frames in JIT, Native AOT and JIT with hardware
intrinsics disabled (504 comparisons).

| Runtime and workload | Frames/process | Elapsed ms/frame | CPU ms/frame | Peak RSS, MiB |
| --- | ---: | ---: | ---: | ---: |
| JIT, Spot triangles, 384 × 384, 1 sample, plain | 72 | 7.97 → 6.53 | 10.44 → 9.02 | 51.3 → 51.2 |
| JIT, Spot triangles, 384 × 384, 1 sample, full | 60 | 23.16 → 20.64 | 32.56 → 29.61 | 52.8 → 52.9 |
| JIT, Spot quads, 512 × 512, 4 samples, full | 20 | 107.15 → 96.59 | 172.90 → 161.98 | 121.8 → 122.1 |
| JIT, Dog assembly, 384 × 384, 1 sample, full | 36 | 30.95 → 21.66 | 35.10 → 26.56 | 128.8 → 128.8 |
| Native AOT, Spot triangles, 384 × 384, 1 sample, plain | 72 | 7.82 → 6.14 | 10.46 → 8.67 | 26.2 → 26.3 |
| Native AOT, Spot triangles, 384 × 384, 1 sample, full | 60 | 22.98 → 20.09 | 32.14 → 29.22 | 27.7 → 27.8 |
| Native AOT, Spot quads, 512 × 512, 4 samples, full | 20 | 106.85 → 92.66 | 168.31 → 160.88 | 95.9 → 96.0 |
| Native AOT, Dog assembly, 384 × 384, 1 sample, full | 36 | 31.82 → 22.06 | 36.53 → 27.19 | 97.2 → 98.2 |

Separate instrumented JIT runs at 384 × 384 timed preparation, the main depth
pass, the shadow-map pass and final shading. Each value is the mean of 30
measured frames in each of two runs per version, with reversed run order.
Both depth stages include their camera setup and buffer allocation. On Spot,
final shading, including shadow filtering, ambient occlusion and RGBA output,
accounted for 69% of the new frame time in these runs.

| Model | Preparation, ms | Main depth, ms | Shadow map, ms | Shading and output, ms |
| --- | ---: | ---: | ---: | ---: |
| Spot triangles | 1.50 → 0.71 | 3.52 → 2.56 | 5.10 → 2.69 | 12.33 → 13.30 |
| Dog assembly | 9.44 → 4.60 | 7.00 → 4.92 | 8.81 → 5.50 | 6.25 → 6.73 |

## Screen-space ambient occlusion during rotation

Starting at `085eec8d`, four JIT runs per configuration isolated the effects
on triangulated Spot at 384 × 384, one sample per pixel. Each run timed the
shading and output stage over 36 angles after 12 warm-up frames. The camera
and shadow-map depth passes were timed separately. Median shading time was
2.94 ms/frame with both effects off, 5.10 ms with shadow filtering alone,
9.55 ms with ambient occlusion alone and 11.46 ms with both enabled.

The ambient occlusion kernel now evaluates four directions together on CPUs
with 128-bit SIMD. It samples the same eight directions at three distances,
keeps a separate horizon for each direction, and sums them in the original
order. The scalar path reads the same sampling pattern. Both use a fixed-size
table per frame.

Across nine configurations, all 168 rotated RGBA frames matched the baseline
byte for byte in each of JIT, Native AOT and JIT with hardware intrinsics
disabled. Cases include odd dimensions, both Spot meshes, the Dog assembly,
one to sixteen samples per pixel and all four combinations of shading effects.

Whole-frame measurements use four alternating baseline/candidate process pairs
per case on the two pinned cores above. Each process renders 12 warm-up frames,
then 72 measured frames on Spot triangles, 20 on Spot quads or 36 on Dog.
The table reports medians of per-process measurements. P95 is the 95th
percentile frame time within a process. All cases except Spot quads use pooling.

| Runtime and workload | Mean ms/frame | P95 ms/frame | CPU ms/frame | Peak RSS, MiB |
| --- | ---: | ---: | ---: | ---: |
| JIT, Spot triangles, 384 × 384, 1 sample, ambient occlusion only | 23.63 → 20.68 | 32.78 → 28.26 | 24.00 → 19.77 | 51.3 → 51.4 |
| JIT, Spot triangles, 384 × 384, 1 sample, full | 32.43 → 27.05 | 45.31 → 40.44 | 32.88 → 27.71 | 52.8 → 53.0 |
| JIT, Spot quads, 512 × 512, 4 samples, full | 93.78 → 86.64 | 116.93 → 111.51 | 160.96 → 141.70 | 122.2 → 122.1 |
| JIT, Dog assembly, 384 × 384, 1 sample, full | 23.33 → 22.41 | 27.69 → 28.73 | 28.69 → 26.91 | 128.9 → 128.8 |
| Native AOT, Spot triangles, 384 × 384, 1 sample, ambient occlusion only | 12.91 → 10.37 | 16.32 → 12.28 | 21.35 → 16.97 | 26.5 → 26.1 |
| Native AOT, Spot triangles, 384 × 384, 1 sample, full | 30.96 → 25.79 | 46.81 → 33.60 | 29.55 → 24.60 | 27.9 → 28.0 |
| Native AOT, Spot quads, 512 × 512, 4 samples, full | 182.10 → 140.74 | 219.09 → 176.01 | 174.44 → 156.05 | 96.0 → 96.0 |
| Native AOT, Dog assembly, 384 × 384, 1 sample, full | 29.27 → 29.63 | 38.53 → 45.38 | 35.04 → 32.29 | 98.4 → 98.2 |

Two additional pairs of instrumented JIT runs on Spot timed shading and output.
With ambient occlusion enabled and shadows off, shading took 11.83 → 8.79 ms.
With both effects off it took 3.70 → 3.61 ms. Subtracting the latter gives an
estimated ambient occlusion cost of 8.13 → 5.18 ms/frame. With both effects
enabled the measured shading stage took 15.08 → 11.58 ms.

Dog showed smaller, inconsistent elapsed-time changes. Four more JIT process
pairs at 144 frames each measured 27.50 → 28.04 ms/frame elapsed, 36.35 → 39.88 ms
at P95, and 32.64 → 32.30 ms/frame CPU. Peak RSS was approximately 128.9 MiB in both versions.
Managed allocation varied from about 3.9 to 31.2 KB/frame across processes in
both builds.

With all hardware intrinsics disabled, three JIT process pairs on 384 × 384
Spot with ambient occlusion alone took 213.50 → 230.88 ms/frame elapsed and
352.75 → 367.52 ms/frame CPU (36 frames per process). This mode uses scalar
offsets and the scalar neighbor loop. The measured speedup applies to the
hardware-accelerated path.
