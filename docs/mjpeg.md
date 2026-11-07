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

## Complete decode performance

The pixel writer advances component coordinates incrementally and retains the
same integer bilinear rounding, clamped edges, orientation and chroma phase.
Coordinate divisions occur at row setup; power-of-two interpolation divisors
use exact shifts. No component-sized temporary plane or output copy is added.
Structural references cover all sampling factors from one to four, eight
orientations, both chroma phases, odd dimensions and padded component strides.

A generated five-second 1280×720, 24 fps, 4:2:0 MJPEG AVI was decoded to native
RGBA and SHA-256 hashes for all 120 pictures. Three before/after process pairs
used one pinned CPU, `DOTNET_PROCESSOR_COUNT=1` and disabled tiered compilation;
opening, decoding, hashing, output and disposal are included. Median results:

| Measure | Before | After |
| --- | ---: | ---: |
| CPU | 4864.937 ms | 3172.508 ms |
| Elapsed | 4886.512 ms | 3192.539 ms |
| Peak RSS | 74.70 MiB | 73.13 MiB |
| Total managed allocations | 588.56 MiB | 588.70 MiB |

Every picture hash is identical across all six runs. This is a complete decode
improvement of about 35% CPU on this source, not a claim about all JPEG files or
transcoding speed. Allocation totals use the default unpooled decoder storage;
callers can provide a shared pool through the public decode-session overload.
The reproducible command is the comparison tool's `video input.avi output.sha256`;
`video-pooled` runs the same operation with caller-owned pooled storage.
