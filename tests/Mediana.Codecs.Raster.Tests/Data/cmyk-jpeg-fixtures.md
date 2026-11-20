CMYK JPEG and JPEG-compressed TIFF references use a 37 by 29 procedural image.
Each ink channel is `(17*x + 13*y + 37*channel) mod 256`.
Pillow 13.0.0 with libjpeg-turbo/libtiff produced the compressed images and
the corresponding RGBA files. JPEG quality is 90; TIFF uses its default JPEG quality.

The plain CMYK file encodes complemented ink input and omits APP14, retaining
the C/M/Y/K component identifiers. Its reference complements Pillow's decoded
CMYK channels before conversion to RGB.

The YCCK file combines independently encoded YCbCr ink samples and an inverted
grayscale black scan. Its frame declares four components and Adobe transform 2.
Pillow independently decodes the assembled multi-scan JPEG to the RGBA reference.

The YCbCr TIFF contains a complete JPEG with Exif orientation 6. The TIFF declares
orientation 1, so the embedded orientation must not rotate its decoded block.
Its subsampling is 1:1 and its procedural RGB channels are `17*x`, `23*y` and
`11*(x+y)`, reduced modulo 256. Pillow/libtiff supplies the reference pixels.
