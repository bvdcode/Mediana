# VP8 video decoding

`Mediana.Codecs.Vp8` provides `Vp8DecoderFactory` for complete VP8 coded-frame
packets (`StreamFormats.Vp8`). It reconstructs planar 8-bit YUV420 directly
into caller-budgeted storage. Output and last/golden/alternate references
share immutable buffer leases. Retaining an output frame does not copy its
pixels; it keeps that allocation alive until the client releases it.

The decoder maintains frame probabilities, segmentation, reference updates,
sign biases, split motion vectors and loop-filter state. Hidden pictures
update decoding state without producing a displayed frame. Reset requires
a new keyframe. Keyframes may change picture dimensions; existing output
leases remain valid. Container color and pixel-geometry descriptions retain
their common precedence. Reserved bitstream versions and color spaces are
rejected.

The shared Matroska reader accepts both `matroska` and `webm` document types
and maps `V_VP8` without requiring CodecPrivate. Unsupported alpha or stereo
track presentation remains outside this mapping. The CLI registers the
same public decoder used by library clients. The IVF files in the test
corpus supply coded packets; an IVF container reader is a separate feature.

Correctness checks compare every visible YUV sample against independent
native decoding on eight synthetic streams: versions 0–3, odd dimensions,
noise, motion, repeated keyframes and four hidden alternate-reference
pictures. The same corpus is decoded again after reset. WebM integration
checks whole-file decoding, repeated indexed seeks, complete AVC encoding,
allocation budgets and reference ownership. These checks cover a defined
corpus and do not establish conformance for every possible VP8 stream.

- [VP8 format and decoding guide, RFC 6386](https://www.rfc-editor.org/rfc/rfc6386)
- [Matroska VP8 mapping](https://www.matroska.org/technical/codec_specs.html#v_vp8)
- [Reconstruction corpus](../tests/Mediana.Tests/Data/Vp8/README.md)

## Complete decode performance

A single pinned CPU decoded and hashed every visible YUV row, writing one
SHA-256 record per picture. Fresh .NET processes used a shared buffer pool,
one processor and disabled tiered compilation. FFmpeg used one decoder
thread and the same SHA-256 frame output. These are complete operations,
including opening, decoding and output, rather than isolated transform timings.

Three alternating runs of a synthetic 1280x720, 24 fps, ten-second VP8 file
showed the following medians before and after using coefficient end positions
to select the DC reconstruction path directly:

| Metric | Before | After |
| --- | ---: | ---: |
| CPU time | 5.303 s | 2.989 s |
| Elapsed time | 5.423 s | 2.999 s |
| Peak RSS | 57.92 MiB | 57.87 MiB |
| Managed allocations | 12.35 MiB | 12.41 MiB |

All 240 per-picture hashes match both versions and FFmpeg. FFmpeg's three-run
median CPU was 1.603 s and peak RSS was 68.12 MiB. This workload remains about
1.86 times slower than FFmpeg; the memory figures describe this process and
corpus, not universal bounds.

A full minute constructed from six repetitions of the source contained
1,440 pictures. CPU decreased from 31.317 to 16.156 s; elapsed time decreased
from 31.760 to 16.517 s. Peak RSS was 64.91 and 65.43 MiB respectively, with
24.63 MiB managed allocations for both. All 1,440 hashes match FFmpeg, which
used 7.839 CPU seconds and 67.86 MiB peak RSS. This is a duration check on a
repeated source, not an independently varied minute of content.

A separate 640x360 generated-noise source (96 pictures) reduced median CPU
from 1.136 to 0.945 s across three runs. Median peak RSS was 51.52 and
51.86 MiB; managed allocations were 3.01 and 3.60 MiB. All before/after
picture hashes match. Allocation differences include process startup and
pool behavior; no image-sized allocation was added by the change.

The sampling profile attributed about 682 ms to repeatedly scanning the
coefficient array from the inverse-transform path. The entropy reader
already knows the final nonzero position, so reconstruction now uses that
value. General transforms and the WebP encoder retain their common checked
entry point. The eight exact reconstruction streams, the scalar execution
path, 199 WebP tests and the complete Native AOT executable passed after
this change.
