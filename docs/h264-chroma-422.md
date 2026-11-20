# Progressive H.264 4:2:2 decoding

`H264DecoderFactory` decodes progressive 8-bit planar 4:2:2 I/P/B pictures using
CAVLC or CABAC and flat scaling lists. Returned frames use
`VideoPixelFormat.Yuv422P8`: separate Y, Cb and Cr byte planes, half-width chroma
and full-height chroma. Cropping, timestamps and retained frame ownership use the
same public decoding and seeking APIs as 4:2:0.

Picture storage, prediction blocks and frame publication use independent
horizontal and vertical chroma scales. Intra chroma prediction covers 8×16
samples. Residual reconstruction reads eight chroma DC coefficients per plane,
uses the 2×4 inverse DC transform and reads all eight AC blocks. Motion
compensation resolves chroma motion to eighth-sample horizontal and
quarter-sample vertical positions. Deblocking keeps full-height vertical chroma
edges and horizontal chroma edges that remain necessary with luma 8×8 transforms.
Both SIMD and scalar filtering support this geometry.

Four independent FFmpeg 6.1.1/libx264 files exercise CABAC, CAVLC, QP1/22/40,
multiple slices, I/P/B ordering, three GOPs and cropped coded borders. All 96
frames and 96 reverse seeks match the reference pixels exactly. Tests also check
movie presentation times, frame durations, format descriptions and released
buffers. Standalone Native AOT validation passes the same sequences and
independent filtering equations. Scalar validation passes with hardware
intrinsics disabled. The H.264 test selection passes 843 tests.

Reproduction commands and reference hashes are in the
[MP4 fixture documentation](../tests/Mediana.Tests/Data/Mp4/README.md#progressive-high-422-sequences).
The shared sequence checks run from managed tests and the Native AOT test runner.

[High10 and 10-bit 4:2:2](h264-high10.md) extend the same pipeline. Interlaced
reconstruction, custom scaling matrices and transform bypass remain separate decoder work. The encoder continues to accept
8-bit 4:2:0. This change does not establish a decoding performance comparison.
