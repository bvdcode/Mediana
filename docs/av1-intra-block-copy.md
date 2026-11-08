Mediana.Codecs.Av1 supports intra block copy through the ordinary public decoder
factory. No separate conversion path or external decoder is required.

The frame header preserves `allow_intrabc`, including its normative exclusion
when super-resolution changes the coded width. Intra-copy blocks use a separate
adaptive flag and motion-vector entropy context. Candidate motion comes from
previously decoded intra-copy blocks, with the specified default vector when
the candidate stack is empty. The current frame supplies prediction samples;
previously published picture storage remains immutable.

Luma vectors are whole-pixel displacements. Subsampled chroma can therefore need
half-sample bilinear prediction. The kernel supports byte and ushort samples
without allocating temporary arrays. Every vector is checked for signed range,
whole-pixel precision, full coded-block bounds, shared sub-8 chroma samples,
tile membership and the superblock wavefront delay before prediction reads.
Motion metadata reuses the existing sixteen-byte block grid and remains charged
to the caller's allocator. Loop filter, CDEF, restoration and delta-filter syntax
are excluded when required by `allow_intrabc`; quantization and inter transform
selection retain their normal syntax.

Seven procedural libaom 3.8.2 streams cover 21 pictures: color, odd dimensions,
monochrome, 128-pixel superblocks, a two-by-two tile grid, subsequent inter
pictures, and lossless coding with palettes. All 5,167,308 visible sample bytes
match independent dav1d 1.4.1 output. Tests repeat decoding after reset, check
random-access packet metadata and held-output ownership, and verify that memory
budget failures and cancellation release partially allocated frame storage.
Pure-kernel tests additionally cover all bilinear phases at eight, ten and
twelve bits. The complete existing AV1 suite passes in both normal and scalar modes;
a strict Native AOT consumer repeats all 42 picture hashes, ownership checks and
48 pure-kernel cases. These checks do not imply complete high-bit-depth frame decoding.

JIT allocation checks run in a checked no-GC interval with a positive allocation
control and retain the exact zero-byte requirement. This isolates the counter
from the background-collection accounting effect reproduced by an empty
SpinWait interval on .NET 10.0.14 and reported in
[dotnet/runtime #134724](https://github.com/dotnet/runtime/issues/134724).

The implementation follows the approved
[AV1 Bitstream and Decoding Process Specification](https://aomediacodec.github.io/av1-spec/av1-spec.pdf):
sections 5.9, 5.11.7, 5.11.26, 5.11.31, 6.10.25, 7.10.2 and 7.11.3.
