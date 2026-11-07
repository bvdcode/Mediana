# VP9 video decoding

`Mediana.Codecs.Vp9` exposes `Vp9DecoderFactory` for VP9 access units
(`StreamFormats.Vp9`). Register it with `MediaEngineBuilder` and use the
shared Matroska reader for WebM or MKV. The CLI uses the same factory.

The decoder reconstructs profiles 0–3 with 8-, 10- and 12-bit samples,
4:2:0, 4:2:2, 4:4:0 and 4:4:4 sampling, planar RGB, lossless transforms,
segmentation, compound and scaled motion prediction, loop filtering and
between-picture probability adaptation. Hidden pictures update reference
state; show-existing pictures retain reference pixels with the current
packet's presentation timing. Superframes may prefix a visible picture
with hidden pictures. An access unit carries at most one visible picture
and its timestamp; multiple visible pictures require separate timed inputs.

Output uses caller-budgeted native planar storage. Precision above eight
bits occupies the low bits of native-endian unsigned 16-bit words.
`PlanarVideoPixelFormat` describes color model, precision and sampling;
RGB plane order is G/B/R. Output frames and reference pictures share immutable
leases. Retaining a frame keeps its allocation alive without copying pixels.
Cancellation, reset, failure and disposal release decoder-owned leases.
Reset requires a new keyframe.

The shared processing API converts these planar formats to SDR sRGB previews
without a full-resolution RGB intermediate. Explicit video preparation can
reduce precision and chroma sampling directly before AVC encoding. The
sequence preparation can accept source resolution changes when they resolve
to the same configured output geometry and color interpretation. It rejects
changes requiring a new encoder configuration. HDR tone mapping is a separate
operation.

Container color and sample geometry descriptions keep their normal precedence.
Native VP9 rendering dimensions determine sample aspect when the container
does not override it. The common indexed video session supplies seeking;
there is no separate VP9 demuxer or seek pipeline. IVF is used for packet
fixtures and is not registered as an input container.

## Verification

The reconstruction corpus contains 14 independently encoded sequences.
Every visible picture is compared by native-plane SHA-256, including repeated
decoding after reset. Coverage includes all four profiles, all supported
sampling layouts, RGB, odd dimensions, multiple tile rows/columns, lossless
pictures, error resilience, hidden alternate references, probability
adaptation, and resolution changes using scaled inter references. WebM tests
exercise complete decoding, previews, repeated indexed seeks and complete
AVC encoding. Independent kernel fixtures also cover arithmetic decisions,
transforms, intra prediction, motion vectors, interpolation and filtering.
The complete Native AOT executable and the VP9 suite with hardware intrinsics disabled also passed. These checks cover the recorded corpus rather than every conforming stream.

## Complete decode performance

Fresh processes opened WebM, decoded and SHA-256-hashed every visible plane,
and wrote one hash record per picture. Both implementations ran on one
pinned CPU; FFmpeg used one decoder thread. The .NET comparison disabled
tiered compilation and used the shared memory pool. Timings include opening,
decoding and output, not only the computational kernels.

Three alternating runs of a synthetic 1280 × 720, 24 fps, ten-second source
gave these medians:

| Metric | Before | After |
| --- | ---: | ---: |
| CPU | 5.255 s | 3.473 s |
| Elapsed | 5.255 s | 3.473 s |
| Peak RSS | 62.10 MiB | 60.48 MiB |
| Managed allocations | 18.20 MiB | 16.15 MiB |

The changes reuse coefficient storage across blocks, copy integer-position
predictions directly, vectorize integer compound averaging and compute filter
parameters for four adjacent lines together. A sampling profile identified
buffer clearing and filter setup as substantial costs. All 240 picture hashes
match before, after and FFmpeg. FFmpeg's median CPU was 1.220 s, so this
workload remains approximately 2.85 times slower.

A minute made from six repetitions of the source contained 1440 pictures.
CPU decreased from 30.085 to 18.976 s. Peak RSS changed from 66.29 to
66.50 MiB, and managed allocations from 27.34 to 27.48 MiB. All hashes match
FFmpeg, which used 7.319 CPU seconds. This checks sustained duration on a
repeated source, not a separately varied minute.

A separate 640 × 360 source with generated temporal noise contained 96
pictures. CPU decreased from 1.201 to 1.115 s; peak RSS was 52.26 and
52.43 MiB, with 4.13 and 4.33 MiB managed allocations respectively. All
hashes match FFmpeg, whose CPU time was 0.224 s. The smaller gain and larger
remaining gap on noise show that the first source does not represent all
VP9 workloads.

The comparison entry point is `video-pooled` in
`benchmarks/Mediana.MediaComparison/Operations`. Native libraries are used
only for independent reference generation and comparison, not decoding in
Mediana.

- [VP9 bitstream and decoding specification](https://storage.googleapis.com/downloads.webmproject.org/docs/vp9/vp9-bitstream-specification-v0.7-20170222-draft.pdf)
- [Matroska VP9 mapping](https://www.matroska.org/technical/codec_specs.html#v_vp9)
- [Reconstruction corpus](../tests/Mediana.Codecs.Vp9.Tests/Data/README.md)
