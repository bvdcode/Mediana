# TGA images

`Mediana.Codecs.Tga.TgaImageDecoder` provides the shared decode and inspection
interfaces and is registered in CLI and embedded-image defaults.

Implemented raster variants are types 1/2/3 and RLE 9/10/11: indexed 8/16-bit
indices with nonzero palette origins, 15/16/24/32-bit color entries and pixels,
and 8-bit gray or 16-bit gray/alpha. All four image origins and legacy two-way
and four-way scan-line interleaving produce top-down RGBA8. No intermediate
image or expanded palette is allocated.

TGA 2.0 extension and developer offsets delimit the raster. Extension alpha
types 0/1 discard unused alpha, 3 retains straight alpha, and 4 unpremultiplies
associated alpha. Undefined alpha type 2 is rejected during decoding. Color
correction tables and arbitrary gamma curves are not implemented; linear,
gamma-2.2 and gamma-2.8 declarations are retained. Missing color primaries or
transfer information remain unspecified for the caller's color policy.

Inspection reports dimensions, native origin and descriptor, type, depth,
palette count, binary image ID and declared extension gamma/alpha properties.
It does not decompress RLE pixels. Invalid headers, metadata offsets, palette
indices, truncated pixels, oversized runs and excess raster data are rejected.
The output uses the shared allocator, source pixel limit and cancellation.

Ten independently encoded Pillow 12.3 images cover RGB, RGBA, gray, gray/alpha
and indexed images in raw and RLE form, with byte-exact complete references.
Additional tests cover origin/interleave mapping, extension alpha semantics,
nonzero palette origins, malformed runs and release after failure.

The raster layout and extension interpretation were checked against
[ImageMagick's TGA implementation](https://github.com/ImageMagick/ImageMagick/blob/main/coders/tga.c)
and the [TGA format description](https://www.fileformat.info/format/tga/egff.htm).
