# Raster reference corpus

These small procedural images exercise bitmap/icon palettes, bitfields and RLE;
GIF LZW, interlace and disposal; TIFF compression, pages, planes, tiles and alpha;
and WebP transforms, entropy groups, alpha and animation.

BMP/GIF/TIFF and VP8L references are compared byte-for-byte. Fully transparent
GIF RGB is ignored. TIFF JPEG and VP8 allow four RGB levels of rounding difference;
alpha is exact. TIFF associated 16-bit references use integer normalization before
8-bit quantization, including small nonzero alpha values.

Fixtures and independent RGBA references were produced using Pillow/libtiff,
FFmpeg, libwebp and tifffile. WebP animation references use the raw libwebp canvas
bytes, including the alpha channel when a wrapper reports RGBX. Lossless encoders
use exact transparent RGB where applicable. JSON manifests record geometry,
page/frame references and allowed error.

The RAW metadata fixtures contain a thumbnail directory and a larger CFA SubIFD
with no encoded samples. They verify sensor-directory selection for DNG, CR2, NEF
and ARW and that Sony make alone does not identify an ordinary TIFF as RAW.

No runtime decoding dependency on these reference tools is required.

`qoi-reference.qoi` is a procedural 37×29 straight RGBA image encoded by
Pillow 12.3. Channels are `(19*x + 31*y + 23*c) mod 256`, except every ninth
column uses `17*c`. The complete `.rgba` reference retains transparent RGB.
`pnm-reference.ppm` stores the same pattern's RGB channels using Pillow 12.3;
its reference adds an opaque alpha channel without changing the sample values.
The `tga-*` fixtures encode the same source in Pillow 12.3 RGB, RGBA, L, LA and
palette modes, both raw and RLE. Complete decoded RGBA files are the oracle.
`ani-sequence.ani` wraps the independent PNG/DIB icon fixtures in RIFF ACON,
with stored-frame sequence 1,0,1 and durations 2,3,4 jiffies.

`webp-vp8-odd-padding.webp` is an authored 33×35 RGBA pattern, with channel
`c = (7*x + 11*y + 19*channel) mod 256`, encoded by Mediana as lossy WebP at
quality 2. Its ALPH payload is 184 bytes and its VP8 payload is 275 bytes, followed
by the required zero RIFF padding byte. The independent complete RGBA reference
uses Pillow 12.3/libwebp. It exercises an odd final VP8 chunk in an extended file.

`jpeg-encode-reference.rgba` is the complete Pillow 12.3/libjpeg decode of
Mediana baseline JPEG encoding of `qoi-reference.rgba`: quality 75, 4:2:0,
white alpha background, square samples. Comparison allows three RGB code values
for decoder IDCT and chroma reconstruction rounding; alpha must match exactly.

`metadata-reference.pcx` is Pillow 12.3 RGB PCX encoding of the procedural
37×29 QOI reference. `metadata-reference.psd` and `.psb` use Adobe's documented
headers/resources and contain the same planar RGB image, a 7×5 Pillow JPEG
thumbnail (`metadata-preview.jpg`), and an EXIF resource with example camera,
orientation and date values. Pillow independently decodes the complete PSD
merged image to the source RGB bytes. PSB exercises 64-bit layer-section lengths.

`metadata-sensor-*` and `metadata-maker-*` are procedural metadata-only RAW
fixtures. They declare distinct 4032×3024 sensor and 7×5 preview sizes; the
preview bytes are the existing Pillow-produced `metadata-preview.jpg`.
Both TIFF byte orders, EXIF capture fields and Nikon/Olympus/Pentax offset bases
are checked against ExifTool. `metadata-sensor.raf` declares a separate crop.
These fixtures contain no encoded sensor samples.

`metadata-reference.avif` is encoded by Pillow/libavif from the existing 37×29
RGB reference. `metadata-oriented-thumbnail.avif` is a procedural HEIF container
using independently encoded AV1 primary/thumbnail items; it adds a 90-degree
clockwise transform, EXIF and an original 7×5 thumbnail. ExifTool confirms the
stored dimensions, transform and capture fields.

`metadata-sensor.cr3` is a procedural Canon ISO BMFF fixture with independently
encoded JPEG data in THMB, PRVW and an indexed CRAW/JPEG sample. EXIF, GPS and
Canon sensor declarations deliberately distinguish 4032×3024 sensor, 4000×3000
rendered image and 7×5 preview. ExifTool independently reads these declarations.

`exr-{half,float}-{none,rle,zip1,zip16}.exr` were encoded with FFmpeg from a
procedural 17×19 premultiplied linear RGBA chart, including negative and HDR
samples. An explicit square-pixel aspect writes the required EXR attribute.
`exr-{half,float}.rgba-f32` contains complete native reference decoding exported
as little-endian interleaved float RGBA; OpenEXR 3.5.4 independently matches it.
`exr-multipart.exr` and its two reference planes were written/read by OpenEXR
3.5.4: mixed HALF/FLOAT, extra UINT channel, named color layer, Y-only second
part, negative origin, distinct display window, 3:2 pixel aspect and P3 primaries.
`exr-nonfinite.exr` retains valid IEEE infinities/NaN and extended-range Y samples.

`exr-unicode.exr` is written and decoded with OpenEXR 3.5.4. Its selected U+10000 layer and an auxiliary U+E000 channel verify UTF-8 channel ordering; every selected pixel is RGBA (0.25, 0.5, 1, 0.5).
