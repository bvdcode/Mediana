# Exif JPEG presentation references

These 38 authored JPEGs reuse the original synthetic sequential/progressive
JPEG corpus and add TIFF metadata in both byte orders. The 32 orientation cases
cover every Exif transform, including transpose/transverse and odd dimensions.
Other cases cover JFIF coexistence, the default orientation, an independent
thumbnail orientation, grayscale and co-sited 4:2:2/4:2:0 chroma.

Expected complete RGBA pixels come from Pillow/libjpeg and ImageOps.exif_transpose.
Co-sited references use FFmpeg's independent native component decoding, followed
by a floating-point bilinear reconstruction on the Exif top-left chroma grid.
The reconstruction keeps odd-edge sample spacing instead of resizing the
rounded-up chroma extent to the luma extent. All are sRGB with
square pixels. corpus.json records dimensions, orientation and input SHA-256.
There are no external photographs or embedded third-party content.
