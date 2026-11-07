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

`webp-vp8-odd-padding.webp` is an authored 33×35 RGBA pattern, with channel
`c = (7*x + 11*y + 19*channel) mod 256`, encoded by Mediana as lossy WebP at
quality 2. Its ALPH payload is 184 bytes and its VP8 payload is 275 bytes, followed
by the required zero RIFF padding byte. The independent complete RGBA reference
uses Pillow 12.3/libwebp. It exercises an odd final VP8 chunk in an extended file.
