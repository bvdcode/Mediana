# JPEG reference images

These original test patterns and quantized coefficient sequences are distributed
under the repository's MIT license. The complete `.rgba` references were decoded
independently with Pillow 12.3.0 / libjpeg-turbo. Tests do not invoke Pillow or an
external media process.

`corpus.tsv` identifies 23 complete images. Grayscale and YCbCr inputs cover 1x1,
7x9, 16x16 and 35x19 dimensions, 4:4:4/4:2:2/4:2:0 sampling and both standard and
optimized Huffman tables. The 73x35 restart cases use an interval of one MCU and
exercise the complete modulo-eight marker sequence more than once.

`multiscan-420` and `multiscan-444` use an independently authored JPEG writer:
each component has its own sequential scan, fixed canonical Huffman codes,
known signed DC/AC coefficients, long zero runs and a quantization table redefined
before that component's scan. Odd edges require partially visible blocks.
`extended-wide-quantization` uses SOF1, table selector three and 16-bit DQT entries.
These files declare JFIF sample aspect ratio 3:2; Pillow-authored JFIF files use 1:1.

`adobe-rgb` stores RGB components with Adobe APP14 transform zero. It has no pixel
aspect declaration; the library retains that absence. `embedded-cover` is the
existing 24x16 JPEG from `../Metadata/cover.jpg`, also embedded as the second
picture in the real FLAC and M4A fixtures. It has no JFIF/Adobe declaration. Its
known full-range YCbCr and square-pixel interpretation is supplied explicitly in
library tests; component identifiers alone do not establish that interpretation.

All RGB components and opaque alpha values are compared. The permitted error is
four 8-bit codes per color component for independent IDCT and chroma interpolation
rounding; opaque alpha must match exactly. The real cover guard permits two codes.
Managed/native byte equality is checked separately from this numeric tolerance.
`unsupported-progressive.jpg` verifies explicit rejection of SOF2.
