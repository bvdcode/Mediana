Managed AV1 reconstruction currently covers Main-profile eight-bit key pictures with lossless or four-point quantized
transforms, monochrome or 4:2:0, with a single tile inside a complete frame
OBU. The public factory accepts version-one av1C configuration and low-overhead
OBUs with explicit sizes. Output keeps native planar samples, packet timing,
render aspect ratio and codec color labels without copying pixel storage.

Verified reconstruction includes all partition shapes, palette luma,
nonzero lossless coefficients, directional intra prediction without edge
filtering, chroma prediction from luma, ordinary multi-frame sequence headers,
odd dimensions and partial frame edges. Quantized four-point reconstruction
includes DCT, ADST, identity and horizontal/vertical DCT transform types, all
four coefficient probability groups, chroma quantizer deltas and reduced
transform sets. Independent libdav1d baselines require
exact equality for every visible sample. Entropy fixtures separately check
arithmetic state and CDF adaptation against AOM.

Larger quantized transforms, inter/reference pictures, smooth/Paeth/filter-intra,
deblocking, CDEF, loop restoration, intra-edge filtering, intra block copy,
chroma palette, segmentation, multiple
tiles, separate frame-header/tile-group OBUs, super-resolution, high bit depth,
film grain and decoder-model timing are not implemented. Encountering these
tools fails explicitly. The module is not registered in the default pipeline
or published as a package while ordinary AV1 playback coverage is incomplete.
