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

The complete coded-picture decoder currently accepts eight-bit Main-profile
sequences. Ten-bit decoding still requires depth-aware quantization, transforms,
picture storage, reference prediction and in-loop filters. High-depth prediction
kernel coverage alone does not establish coded-picture support.

Prediction and edge operations follow section 7.11.2 of the
[AV1 specification, version 1.0.0 with Errata 1](https://aomediacodec.github.io/av1-spec/av1-spec.pdf).
Independent vector formats are described with the
[test data](../tests/Mediana.Codecs.Av1.Tests/Data/README.md).
