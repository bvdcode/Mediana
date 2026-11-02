# PNG pixel references

The 36 authored PNG images cover all 15 standard color-type/bit-depth pairs,
non-interlaced and Adam7 image data, all five filters, palette transparency,
grayscale/RGB color keys and straight alpha. The six extra Adam7 images cover
1×1, 1×9, 9×1, 2×2, 3×5 and 5×3 geometries with empty passes. Packed scanline
padding contains nonzero bits. Each file splits zlib headers, data and checksums
across consecutive IDAT chunks, including zero-length chunks.

The images were encoded independently using Python 3 standard-library `struct`
and `zlib` according to the [W3C PNG Third Edition](https://www.w3.org/TR/png-3/).
The `corpus.tsv` entries give the image name and dimensions. Every `.rgba` file
contains all pixels in row-major straight RGBA32 order.

Pillow 12.3.0 independently decoded each image below 16-bit sample depth.
FFmpeg 6.1.1 decoded every 16-bit image to `rgba64be`; each sample was reduced
using `floor(input * 255 / 65535 + 0.5)` from PNG section 13.12. Every independent
reference agreed with the authored source pixels. FFmpeg's `monob` conversion
did not preserve the grayscale 1-bit transparency key, so it is not the alpha
reference for those images.

Each image declares sRGB and pHYs pixels-per-unit (2,3), giving sample aspect
ratio 3:2. Colors are not composited against a background. The files and source
patterns are distributed under this repository's MIT license.

`cover.rgba` is the complete 24×16 RGBA reference for the existing
`../Metadata/cover.png`, independently decoded by both Pillow and FFmpeg with
identical results. The same PNG appears in the MP3, FLAC, M4A and Matroska cover
fixtures; these tests decode its original extracted bytes after container disposal.
