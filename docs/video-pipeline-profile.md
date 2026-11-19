# Complete video pipeline CPU profile

The measured operation reads indexed MP4, decodes H.264/AAC, prepares frames,
encodes H.264/AAC and writes MPEG-TS. A complete 596.48-second movie and a separate
150-frame 720p input were profiled. Codec work dominates; reducing async overhead
alone is not a sufficient optimization strategy for these conversions.

## Conditions and complete outputs

Linux x64, Intel Core i5-12450H, SDK 11.0.100 / Native AOT 11.0.0, logical CPU 5,
process nice -20 and `DOTNET_PROCESSOR_COUNT=1`. Runtime Async is not enabled for
Mediana methods. Media buffers use `MemoryPool<byte>.Shared` through the public
allocator. The operation finishes with zero retained media-buffer bytes.

Both profiles use QP26, CABAC, rate-distortion macroblock analysis, a two-second
keyframe interval and no rate-control target. The movie keeps its original
[Big Buck Bunny](real-hd.md) H.264 video and uses a stereo 128-kbit/s AAC input;
video is reduced from 1280×720 to 640×360. The second input preserves 1280×720 and
has no audio. The benchmark's `--pool --cabac-rd` options select this configuration.

| Complete operation | Movie, 596.48 s | Additional 720p, 5 s |
| --- | ---: | ---: |
| Output pictures | 14,315 | 150 |
| Decoded audio samples/channel | 28,631,040 | — |
| Output bytes | 100,792,252 | 795,804 |
| Elapsed time, including profiler | 390.311 s | 3.819 s |
| Process CPU, including profiler | 380.499 s | 3.819 s |
| Peak RSS, including profiler | 50.1 MiB | 46.9 MiB |
| Managed allocation over operation | 432.8 MiB | 35.9 MiB |

These are single profiled operations, not an uninstrumented throughput study or
a quality/bitrate comparison against another encoder. No speedup is inferred by
comparing them with different presets or inputs.

FFmpeg decoded both complete outputs with `-xerror` and no errors. The movie
contains all 14,315 decoded video pictures and 27,961 AAC frames; the audio encoder
reports 1,024 priming samples and no final padding. The 640×360 video begins at
1 second; the coded AAC timeline starts one priming frame earlier.

## Sampled CPU distribution

gprofng 2.42 clock sampling used a 997-microsecond interval. The following module
groups sum **exclusive** function costs: each sampled function belongs to at most
one group. Runtime helpers, memory copying and operating-system work remain in
the other category rather than being assigned to a codec without evidence.
Values are approximate because the exported per-function times are rounded.

| Own function cost | Movie | Additional 720p |
| --- | ---: | ---: |
| H.264 codec | 62.4% | 89.4% |
| AAC codec | 16.4% | — |
| Frame processing, primarily area resize | 10.9% | <0.1% |
| Core media/storage code | 1.5% | 1.8% |
| Container and byte-source code | <0.1% | <0.1% |
| Other/runtime/system work | about 8.8% | about 9.0% |

Selected **inclusive** costs help locate work within these groups. Rows overlap
and must not be added together. Stack unwinding and inlining can also leave work
outside an enclosing function's recorded inclusive total.

| Function | Movie | Additional 720p |
| --- | ---: | ---: |
| `H264Decoder.DecodePicture` | 36.8% | 37.7% |
| `H264Encoder.TrySend` | 19.4% | 50.7% |
| `AacBandAllocator.Allocate` | 13.0% | — |
| `Yuv420AreaResizer.Render` | 11.1% | — |
| `H264MotionSearch.Select` | 4.7% | 20.7% |

Async reader frames also have large inclusive totals because task completion runs
downstream continuations on their call stacks. For example, the movie's byte-source
`ReadAtAsync` has a recorded inclusive 34.4% but only 0.006 seconds of own CPU.
That inclusive number is not the cost of storage I/O or evidence for replacing
the async architecture.

Both collectors report `Collection interval timer period was changed (997 -> 0);
profile data may be unreliable`, and a variable-clock warning. The timer warning
is recorded immediately before collector shutdown, but its cause is not established.
Sampled CPU covers 378.203 of 380.499 process seconds for the movie and 3.800 of
3.819 seconds for the second input. This is a coverage check, not proof that the
warning is harmless. These profiles support prioritization; exact savings require
paired, uninstrumented measurements and an independent profile where necessary.

## Optimization candidates

1. **H.264 block-state access and entropy decoding.** In the movie, the exclusive
   costs of `SetMotion`, `get_Modes` and `MotionAt` sum to about 6.4% of all CPU.
   Repeated span construction and storage access inside block loops are worth
   checking with cached typed spans while retaining lifetime and boundary checks.
   CAVLC/VLC reading is another material decoding cost. Changes must preserve
   decoded samples, ordering, reference-picture state and malformed-input behavior.
2. **Area-resize arithmetic.** `Render` and `FilterRow` account for about 10.9%
   exclusive CPU in the scaled movie. The current separable filter already reuses
   a bounded row workspace. Vectorizing its weighted accumulation or specializing
   general integer reduction ratios should be evaluated across dimensions and
   chroma locations, preserving the existing exact weights and rounding.
3. **AAC bit-cost recomputation.** `AacChannelEncoder.Count` alone is 5.8% exclusive
   CPU in the movie. Band allocation repeatedly scans a channel after candidate
   changes and invalidates cached channel costs after accepted moves. Incremental
   costs are a candidate, but scale-factor neighbours, zero bands and section
   splitting must remain exact; reducing search quality is not an equivalent change.
4. **H.264 motion search and RD work.** Motion selection reaches 20.7% inclusive
   CPU on the second input. Search/reconstruction work remains content-dependent;
   the movie's smaller share does not justify discarding it as an optimization
   target. Quality, output size and CPU must be measured together.

The separate [Runtime Async comparison](runtime-async-performance.md) covers
before/after CPU and memory observations and retains the existing default.
