# AV1 film grain

Film-grain synthesis follows sections 5.9.30, 6.8.20 and 7.18.3 of the
[AV1 specification, version 1.0.0 with Errata 1](https://aomediacodec.github.io/av1-spec/av1-spec.pdf).
It includes scaling-point interpolation, the normative Gaussian sequence,
autoregressive noise, chroma scaling, block overlap and output-range clipping.

The sample kernels accept 8-, 10- and 12-bit monochrome or 4:2:0 planes.
This kernel coverage does not establish support for high-bit-depth coded
pictures in the complete AV1 decoder. Source and destination storage must
be distinct: displayed grain must not change prediction references.

Scratch storage contains fixed grain templates and lookup tables, plus a
bounded stripe and overlap rows. Its size depends on image width and plane
count, rather than the full image area. It is charged to the supplied media
allocator and released on completion or cancellation.

A compact independent reference covers 192 parameter sets and 277,904
samples. Expected SHA-256 hashes were obtained from libaom 3.8.2 synthesis
with deterministic input planes. Cases include 1–70 pixel widths, 1–73 pixel
heights, every supported sample depth, all autoregressive lags and syntax
shifts, extreme signed coefficients, monochrome, chroma-from-luma, overlap,
full and restricted ranges, and seeds at both ends of the 16-bit domain.
Normal, scalar and strict Native AOT runs match the independent samples.

The eight-bit decoder reads grain parameters from frame headers, including
reference-parameter reuse with a newly signaled seed. A displayed existing
reference retains its original grain state. Grain is applied after picture
reconstruction into separate owned display storage; prediction references
retain the samples before grain synthesis. Pictures without grain keep the
existing buffer-lease path.

Seventeen libaom-generated Main-profile sequences exercise all sixteen
film-grain test parameter sets and a monochrome sequence. The public decoder
matches 408 independent dav1d picture hashes across two decode/reset passes,
including 65×49 visible dimensions. Held display frames remain unchanged
through subsequent decoding, reset and cancellation; media allocations are
released independently when their owners are disposed.
