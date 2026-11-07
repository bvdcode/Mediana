# Theora video decoding

`Mediana.Codecs.Theora` provides `TheoraDecoderFactory` for complete Theora 3.2
packets (`StreamFormats.Theora`) and three Xiph-laced configuration headers.
It decodes 8-bit planar YUV 4:2:0, 4:2:2 and 4:4:4 directly into caller-budgeted
storage. Picture cropping uses the bitstream's lower-left coordinates; the
published frame has positive strides and displays the cropped picture.

Previous and golden reference pictures share immutable buffer leases with
outputs. Retaining a frame does not copy its pixels. Zero-byte data packets
repeat the preceding picture with the new packet's timestamp and duration.
Each codec instance accepts one input at a time and applies output backpressure.
Reset requires a new keyframe. A failed or cancelled input releases reference
and working storage and requires reset or disposal.

The decoder handles block flags, all eight macroblock modes, both motion-vector
codebooks, four-vector chroma prediction, per-block quality indices, configurable
quantization matrices and Huffman trees, DC prediction, signed integer DCT and
loop filtering. Quantization tables are prepared once for a decoding run;
block metadata and coefficient storage are reused. The scalar path uses the
same arithmetic as the SIMD prediction and constant-residual paths.

`OggReaderFactory` exposes Theora alongside Opus and Vorbis through the common
container API. The packet index retains exact frame clocks, keyframe locations,
comments and native stream properties. Both revision-zero and revision-one
granule conventions are handled. Seek and timing queries use the retained index
without reading packet payload. Opening still scans Ogg pages to build and
verify that index; it does not decode pictures or retain compressed video.

Register `TheoraDecoderFactory` with `MediaEngineBuilder.AddVideoDecoder` and
`OggReaderFactory` with `AddContainer`. `OpenVideoAsync`, preview preparation,
indexed seeking and `ProcessAsync` then use the same interfaces as other video
formats. The CLI registers these factories and contains no Theora decoder logic.

## Reconstruction checks

The synthetic corpus covers moving and noisy material, 4:2:0/4:2:2/4:4:4,
odd picture crops, repeated keyframes, a constant-color source, fractional
30000/1001 cadence and multiplexed Theora/Vorbis. Every visible native YUV
plane matches independent FFmpeg decoding. Public codec tests repeat sequences
after reset and retain frames beyond decoder disposal. Zero-byte repeat packets,
backpressure, cancellation and failures at different allocation budgets are
checked separately.

The integer transform is also compared with 512 one-dimensional and 512
two-dimensional vectors from Xiph's generic C implementation, including signed
overflow inputs. Ogg tests cover revisioned granules, nonzero origins,
inconsistent final granules, metadata, presentation intervals and keyframe seek.
The shared video API checks complete decoding, preview generation, repeated
reverse seeks and complete AVC encoding for all nine container fixtures.

## Complete decode performance

The operation opens the file, decodes every picture, hashes every visible YUV
row and writes a SHA-256 record per picture. Fresh .NET processes use a caller
pool, one pinned CPU and disabled tiered compilation. FFmpeg uses one decoder
thread and equivalent SHA-256 frame output. Timings include startup and output.

Three alternating runs of a synthetic ten-second 1280×720, 24-fps source give
these medians before and after block prediction and active-coefficient changes:

| Metric | Before | After |
| --- | ---: | ---: |
| CPU | 2.885 s | 1.699 s |
| Elapsed | 2.886 s | 1.699 s |
| Peak RSS | 58.16 MiB | 58.21 MiB |
| Managed allocations | 13.52 MiB | 13.51 MiB |

All 240 hashes match both builds and FFmpeg. One FFmpeg reference run used
1.157 CPU seconds, so the optimized operation remains about 1.47 times slower
on this workload.

A minute made from six repetitions contains 1,440 pictures. CPU decreased
from 16.514 to 9.279 s; elapsed decreased from 16.678 to 9.279 s. Peak RSS
was 62.48 and 62.92 MiB, with 20.07 MiB managed allocations in both runs.
FFmpeg used 6.970 CPU seconds. All picture hashes match. This checks duration
scaling on repeated material, rather than a varied minute of content.

A separate noisy 640×360 source contains 96 pictures. CPU was 1.819 s before
and 1.814 s after; this workload showed no material speed improvement. Peak
RSS was 52.14 and 52.32 MiB and managed allocations 4.48 and 4.47 MiB.
FFmpeg used 0.521 CPU seconds, leaving a larger gap for noisy material.
All picture hashes match.

The sampling profile identified per-pixel predictor handling and scans over
already completed coefficient blocks as the main costs. Interior integer
prediction now copies rows, fractional prediction and constant residuals use
SIMD when available, and completed blocks are removed from subsequent token
passes while preserving coding order. No additional image-sized buffer was
introduced.

- [Theora bitstream specification](https://www.theora.org/doc/Theora.pdf)
- [Xiph reference implementation](https://github.com/xiph/theora)
- [Synthetic fixtures and numerical references](../tests/Mediana.Codecs.Theora.Tests/Data/README.md)
