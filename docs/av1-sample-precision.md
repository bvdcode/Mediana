AV1 intra-prediction kernels share one implementation for native byte and
unsigned sixteen-bit sample storage. Generic specialization keeps the eight-bit
buffer size and avoids converting an image into a wider intermediate.

Smooth, smooth-horizontal, smooth-vertical and Paeth prediction use integer
weights without changing sample precision. Recursive filter-intra retains only
three transform rows, clips to the declared sample depth and supports all five
filters. Directional prediction shares edge filtering, corner smoothing,
upsampling and interpolation across sample types; clipping uses the declared
sample maximum.

Independent AOM 3.8.2 kernels verify 10,848 ten- and twelve-bit cases: all nineteen
transform geometries for ordinary prediction, the fourteen valid filter-intra
geometries, every direction and angle delta, both edge-filter types and all edge
availability combinations. Cases include extrema, alternating samples, constant
edges, ramps, random inputs and clipped destinations. Output hashes and untouched
destination regions match in normal, scalar and strict Native AOT execution.
The kernels allocate no managed sample arrays during warmed calls.

A warmed eight-bit JIT comparison on a fixed processor core used six measurements
per implementation in ABBA order, with 60,000 calls per workload and tiered
compilation disabled. Median process CPU time was 547.69 to 567.20 ms for
64×64 smooth/Paeth prediction, 328.75 to 338.22 ms for 32×32 recursive filters,
and 45.24 to 47.32 ms for 16×16 directional prediction. The changes were +3.56%,
+2.88% and +4.60%; output checksums matched and all measured kernel calls allocated
zero managed bytes. These kernel measurements do not establish whole-decoder
throughput or Native AOT performance.

Dequantization selects the normative DC and AC lookup table for eight, ten or
twelve bits. All 1,536 entries match both specification section 7.12.2 and the
AOM 3.8.2 tables. The lookup clamps the quantizer index after each plane delta.
Coefficient reconstruction retains the wide multiplication and 24-bit magnitude
mask before the transform-size division, then clips to the declared signed
precision: 16, 18 or 20 bits. Invalid depths are rejected explicitly.

Inverse DCT and ADST share the existing integer implementation across signed
16-, 18- and 20-bit intermediate ranges. Two-dimensional reconstruction uses
`BitDepth + 8` for rows and `max(BitDepth + 6, 16)` for columns, including the
normative clamp between passes. Identity transforms, rectangular scaling and
flips retain their prescribed rounding. Lossless WHT retains the declared
sample range. Coefficient storage stays thirty-two bits at every sample depth.

Independent AOM 3.8.2 kernels verify 9,561 additional vectors: 5,796 one-dimensional
transforms, 3,474 two-dimensional transforms and 291 lossless blocks. The corpus
covers every supported transform length and rectangular geometry, all valid
transform types and flips, positive and negative impulses, random coefficients,
constant fields and alternating fields. Normal, scalar and strict Native AOT
execution match every output hash without allocating managed kernel buffers.
The native reference also passes undefined-behavior checks.

A second warmed eight-bit ABBA comparison used six measurements per implementation
on the same fixed core with tiered compilation disabled. Median CPU time changed
from 48.99 to 46.59 ms for 250,000 4×4 DCT calls, 89.27 to 84.65 ms for 50,000
8×8 ADST/ADST calls, 219.18 to 217.52 ms for 6,000 32×32 DCT calls and 249.11
to 246.71 ms for 1,500 64×64 DCT calls. Each call included an identical coefficient
reset. Output checksums matched and allocations remained zero. These measurements
cover the transform kernels and coefficient reset only.

The complete coded-picture decoder currently accepts eight-bit Main-profile
sequences. Ten-bit decoding still requires routing sample precision through picture storage,
reference prediction and in-loop filters. High-depth kernel coverage alone does
not establish coded-picture support.

Prediction and edge operations follow section 7.11.2, dequantization follows
section 7.12.2, and inverse transforms follow sections 7.13.2–7.13.3 of the
[AV1 specification, version 1.0.0 with Errata 1](https://aomediacodec.github.io/av1-spec/av1-spec.pdf).
Independent vector formats are described with the
[test data](../tests/Mediana.Codecs.Av1.Tests/Data/README.md).
