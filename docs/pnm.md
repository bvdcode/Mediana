# Portable bitmap, graymap and pixmap

`Mediana.Codecs.Pnm.PnmImageDecoder` implements shared decoding, page selection
and metadata inspection for P1/P4 PBM, P2/P5 PGM and P3/P6 PPM. It is registered
in the CLI and default embedded-image registry.

Plain variants accept whitespace and comments and contain one image. Binary
variants accept concatenated images, with zero-based `ImageDecodeRequest.PageIndex`.
Metadata includes the page count and each page's dimensions, magic number and
declared maximum sample value. Binary payloads are skipped during inspection;
plain rasters must be scanned to validate their token count.

PBM uses most-significant-bit-first pixels, black=1, white=0, and discards row
padding bits. PGM/PPM support maximum values 1–65535, with big-endian 16-bit
binary samples when the maximum is greater than 255. Samples are normalized and
rounded to RGBA8; source precision remains in metadata. PGM/PPM preserve their
specified full-range BT.709 color interpretation. PBM endpoints are sRGB black
and white. There is no inferred alternate gamma interpretation.

The binary header consumes exactly the single whitespace separator defined by
the format. Raster bytes equal to whitespace or `#` are data. Invalid integers,
sample values, geometry, raster lengths, plain extra samples and page selections
are rejected. Output allocation is budgeted, with no intermediate pixel image;
cancellation is checked during header, plain-raster and pixel processing.

Tests cover all six variants, arbitrary maximum values, both binary sample
widths, PBM padding and compact plain bits, mixed binary page sequences,
delimiter-like pixel bytes, malformed numbers, truncation and buffer lifetime.

References: [PBM](https://netpbm.sourceforge.net/doc/pbm.html),
[PGM](https://netpbm.sourceforge.net/doc/pgm.html),
[PPM](https://netpbm.sourceforge.net/doc/ppm.html).
