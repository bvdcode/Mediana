The AV1 decoder applies all fifteen weighted quantization-matrix levels and the
flat level. Luma, U and V select their own levels when the sequence permits
separate chroma parameters. Lossless segments and identity or one-dimensional
transforms use ordinary dequantization, as required by the bitstream.

The 100,320 normative weights are immutable embedded data. Transform lookups borrow
the relevant row-major extent, including the shared thirty-two-sample coefficient
matrices used by sixty-four-sample transforms. Matrix selection adds no frame or
block arrays. The weighted coefficient product uses sixty-four-bit arithmetic
before the normative twenty-four-bit magnitude mask, transform-size division and
eight-bit reconstruction clamp.

All weights and nineteen transform extents at each level match hashes derived
independently from the AOM 3.8.2 inverse matrices, with its rectangular storage
order converted to the normative layout. Nineteen six-picture sequences encoded
with libaom 3.8.2 cover every weighted level, the flat level, odd dimensions,
monochrome and lossless reconstruction. Public decoder output matches libdav1d
1.4.1 hashes for every sample, including a second decode after reset and zero live
owned storage after completion. Scalar execution preserves the same results.

Syntax and arithmetic follow AV1 1.0.0 with Errata 1, sections 5.9.12, 5.9.14,
6.8.11 and 7.12.3, together with the additional quantization tables in the
[approved specification](https://aomediacodec.github.io/av1-spec/av1-spec.pdf).
The [test data description](../tests/Mediana.Codecs.Av1.Tests/Data/README.md)
records fixture formats and independent decoder provenance.
