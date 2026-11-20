# Shared buffer pool in the command-line client

CLI operations supply the standard BCL `MemoryPool<byte>.Shared` to the existing
`MediaBufferAllocator`. The same resource factory serves media and image
commands; no codec-specific allocation policy is introduced. Library callers
continue to select exact allocations or their own pool explicitly.

A Native AOT client compares both allocator policies using the same executable
and public container-to-video session transfer. Each process reads and decodes
all 150 frames four times and hashes every visible sample. Three processes per
policy alternate in ABBAAB order, pinned to logical CPU 11 with `nice -20` and
one processing CPU. No local build or input encoding overlaps timing.

The two sources are the camera inputs from the
[full decoding regression](h264-interlaced.md#progressive-decoding-regression).
All twelve runs decode 600 frames, match independent FFmpeg pixels and release
their live allocation budget. Medians include all four passes and the initial
pool rentals; no warm-up pass is excluded.

| Source | CPU exact / pooled, s | Wall exact / pooled, s | Peak RSS exact / pooled, MiB | Allocations exact / pooled, MiB |
| --- | ---: | ---: | ---: | ---: |
| camera-720 | 8.136 / 7.948 | 8.148 / 7.957 | 59.02 / 33.31 | 1234.49 / 25.87 |
| camera-422-10 | 20.553 / 20.046 | 20.573 / 20.054 | 112.95 / 47.95 | 2588.28 / 40.21 |

CPU medians improve by 2.32% and 2.47% in these runs. The stronger result is
lower cumulative allocation and peak resident memory. This is reuse through an
existing BCL API, not a new .NET 11 feature or a remedy for the encoder's remaining
algorithmic gap. It does not establish the same gains for every CLI format.

The allocator clears rented storage and charges its full capacity to the live
budget, including capacity beyond the requested length. Returning the final
lease releases that charge, while the shared pool may retain idle storage in the
process. The CLI borrows the process-wide pool and does not dispose it. Large
individual rentals can exceed the exact requested size because of pool buckets.

Library consumers can select the same policy without depending on the CLI:

```csharp
MediaOpenOptions options = new();
MediaBufferAllocator allocator = new(options.BufferBudgetBytes, MemoryPool<byte>.Shared);
await using ContainerSession container = await engine.OpenContainerAsync(source, allocator, options);
await using VideoSession video = engine.OpenVideo(container);
```

Frames returned by the session remain independently owned and must be disposed
by the consumer. Disposing the session does not invalidate a frame retained by
the caller.

The Release build passes without warnings. The existing CLI, pooled-buffer,
pixel-buffer ownership and scene-buffer ownership selection passes 459 tests.
