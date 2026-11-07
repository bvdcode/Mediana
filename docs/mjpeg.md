# Motion JPEG video

`MjpegDecoderFactory` decodes complete JPEG pictures through the shared managed
JPEG component decoder. Register it as a video decoder; its codec identifier is
`mjpeg` and packet representation is `StreamFormats.Mjpeg`. Matroska `V_MJPEG`
tracks with progressive display geometry map to this representation without
requiring `CodecPrivate`, following the
[Matroska codec mapping](https://www.matroska.org/technical/codec_specs.html#v_mjpeg).

Each packet carries one complete JPEG picture with its own quantization and
Huffman tables. Baseline, extended sequential and progressive eight-bit Huffman
pictures reuse the existing grayscale/RGB/YCbCr JPEG decoding path. The output is
straight RGBA8 with the packet's timestamp and duration. JPEG image orientation,
density and application metadata do not override video container presentation.
Explicit container SAR, primaries and transfer labels are retained. YCbCr-to-RGB
output currently requires the full-range BT.601 JPEG interpretation; other native
component matrices, interlaced field pairs, arithmetic/lossless JPEG and abbreviated
table-dependent pictures are rejected.

Random-access inspection verifies complete picture markers and self-contained
JPEG header/table declarations without reconstructing samples. No previous
picture or entropy state is retained. Backpressure permits one pending output;
returned frames survive codec reset/disposal. Component planes share one owned
sample allocation, and video output writes directly into its final RGBA buffer.

`VideoPreparationOptions.RgbConversion` explicitly prepares opaque RGB pictures
for AVC encoding through the same orientation/scaling path as other video.
The CLI selects its standard BT.709 limited-range RGB conversion for video/HLS
encoding. Library callers choose the conversion policy themselves. No transparent
pixel is discarded implicitly.

Independent references cover all 36 pictures of 4:2:0, 4:2:2 and 4:4:4 Matroska
sources. FFmpeg-decoded component samples are reconstructed with documented
bilinear chroma and BT.601 equations. Maximum observed RGB differences are
3, 3 and 2 codes, with exact alpha, picture counts, dimensions, timestamps and SAR.
The JPEG still-image corpus additionally covers sequential, progressive,
multiscan, grayscale, restart and storage ownership behavior. Native tests run a
complete pooled Matroska MJPEG → RGB preparation → AVC/MPEG-TS operation and
then decode the source again.
