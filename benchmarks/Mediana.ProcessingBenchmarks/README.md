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
