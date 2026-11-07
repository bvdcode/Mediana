# Video processing measurements

Run `dotnet run --project benchmarks/Mediana.ProcessingBenchmarks -c Release`.
The scaling workload reports elapsed and process CPU time, total managed allocations,
peak live allocator storage and the SHA-256 of the resulting planar pixels.
Each case uses 30 warmup frames and 120 measured frames of deterministic noise.
The same input frame is reused; output allocations and disposal are included.
This measures scaling, not decoding, encoding or complete conversion throughput.

## Exact area scaling

Linux x64, .NET 10.0.14, two runtime processors, tiered compilation disabled for
both implementations. Medians of three alternating runs of the original and
revised algorithm:

| Input → output | Original ms/frame | Revised ms/frame | Speedup | Extra live workspace |
| --- | ---: | ---: | ---: | ---: |
| 1920×1080 → 1280×720 | 34.638 | 7.832 | 4.42× | 20 KiB |
| 1920×1080 → 640×360 | 14.660 | 3.244 | 4.52× | 10 KiB |
| 3840×2160 → 640×360 | 35.239 | 7.080 | 4.98× | 10 KiB |
| 1280×720 → 854×480 | 15.424 | 3.562 | 4.33× | 13.34 KiB |
| 1280×720 → 1280×720, explicit copy | 14.072 | 0.186 | 75.66× | 0 |

The revised filter separates horizontal and vertical integer sums without
intermediate rounding. Its reusable row workspace costs 16 bytes per output
luma column and belongs to the configured allocator. No frame-sized cache is
retained. Explicit identity copies copy visible rows; `ResizeOrRetain` continues
to share the original storage for identity operations.

All five pixel hashes match the original implementation. Independent rational
integrals cover six chroma locations, padded planes, identity, edge extension,
mixed scaling ratios and repeated use. Allocation failure and cancellation
checks verify that sampling plans, workspace and output leases are released.

## Complete indexed pipeline

```sh
dotnet run --project benchmarks/Mediana.ProcessingBenchmarks -c Release -- \
  pipeline input.mp4 output.ts 640 360
dotnet run --project benchmarks/Mediana.ProcessingBenchmarks -c Release -- \
  pipeline input.mp4 interval.ts 640 360 11000 12000
```

Output files must be new. The optional interval bounds are milliseconds on the
movie presentation timeline. All audio/video tracks are encoded to AVC/AAC using
the public processing operation; data tracks are omitted. Input opening, index
queries, decoder warmup, frame preparation, encoding, muxing and cleanup are timed.

Reports include elapsed/process CPU time, first packet readiness, process peak RSS,
total managed allocation, actual source bytes/read calls, output size and per-track
frame/sample counts and codec delay/padding. Process peak RSS includes runtime
startup; total allocation is measured from before source opening through disposal.
Run the built executable directly for external process measurements, excluding the
build launcher. Compare outputs independently for timing, decoded quality and
bitrate before interpreting relative encoder speed.

## Whole-operation before/after

A 60.041667-second, 1280×720, 24 fps CABAC source (1441 pictures), encoded to
640×360 Baseline AVC QP26 and MPEG-TS. Linux x64, .NET 10.0.14, one pinned CPU,
one runtime processor, tiered compilation disabled. Three alternating runs;
only the exact area scaler differs between the two builds. Opening, decoding,
scaling, encoding, muxing and disposal are included.

| Measure | Original scaler | Revised scaler |
| --- | ---: | ---: |
| Median elapsed | 50.188 s | 37.355 s |
| Median process CPU | 48.578 s | 36.273 s |
| Median peak RSS | 112.5 MiB | 111.1 MiB |
| Managed allocation | 3903.7 MiB | 3903.2 MiB |

Every output has 1441 packets and 12,990,988 bytes. All six files have SHA-256
`0156b65d3c89fba4df24e2a1371d2043bbf9a8a0c685d1fa2d5f91bbf114c6fa`.
The full conversion saves about 25% CPU; the larger isolated scaling speedup
does not apply to the complete codec pipeline.

Append `--pool` to select the existing caller-owned `MemoryPool<byte>.Shared`
allocator. Its complete rented capacity is budgeted, including bucket rounding;
idle shared-pool storage is outside the live allocator budget. One additional HD
trial produced the same output hash with 43.3 MiB total managed allocation and
71.4 MiB peak RSS, versus about 3903 MiB and 111 MiB with direct allocations.
CPU was 36.157 s, close to the revised direct-allocation runs. This is an
allocation/RSS observation, not evidence of a further throughput improvement.

A complete 30-minute audiovisual input (96×80, 6 fps, 10,802 pictures and
86,400,000 audio samples) also completed with the pooled allocator: 95.847 s
elapsed, 93.303 s CPU, 54.4 MiB peak RSS and 558.7 MiB total allocation.
Both tracks were encoded, including all 84,375 audio input frames. This small-frame
long recording checks bounded memory and total sample/frame processing; it is not
an HD throughput result.

## Reference and interpolation follow-up

On the same HD source, FFmpeg/libx264 `veryfast`, Baseline, QP26, no B pictures,
GOP48, area scaling and one pinned encoding/filter thread took 7.183 s process CPU
and 8.241 s elapsed. Its MPEG-TS output was 7,598,208 bytes, versus 12,990,988 bytes
for Mediana. Independent source-relative PSNR was 38.972 dB and 40.125 dB,
respectively. Equal quantizers do not establish equal quality or compression;
this observation does not establish encoder parity. A substantial CPU gap remains.

Sampled profiling identified motion interpolation among decoder hot paths.
Interior six-tap blocks now read their reference picture directly; only border
blocks materialize an extended patch. Three further alternating pooled HD runs
gave median CPU 35.938 s before and 34.903 s after, about 2.9% lower. Elapsed
medians were 37.125 s and 35.921 s. Every output retained the hash above.
This is a separate small whole-operation improvement. A tested 16-bit intermediate
change did not improve full-pipeline throughput and is not included.
