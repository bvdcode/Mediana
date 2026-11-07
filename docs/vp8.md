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
