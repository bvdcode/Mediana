The entropy corpus was encoded and independently decoded with AOM v3.13.1
[`entenc.c`](https://aomedia.googlesource.com/aom/+/refs/tags/v3.13.1/aom_dsp/entenc.c)
and [`entdec.c`](https://aomedia.googlesource.com/aom/+/refs/tags/v3.13.1/aom_dsp/entdec.c).
CDF adaptation uses that release's `update_cdf`. These native routines are used
only to produce the fixture; the tests and library are fully managed.

`entropy.bin.gz` contains 600 cases, each with 96 mixed arithmetic symbols,
equiprobable booleans and literals of zero through 32 bits. Alphabets range from
2 through 16 symbols; each has uniform, peaked, rising and end-peaked initial
probabilities, enabled/disabled adaptation, and initial counters 0, 15, 16, 31
and 32. Every operation records its decoded value, arithmetic range, arithmetic
value, bit consumption and updated ascending CDF, including its counter.

The first eight uncompressed bytes are `AV1ENT01`, followed by a little-endian
32-bit case count. A case stores four bytes (alphabet size, probability shape,
adaptation flag, initial counter), 32-bit operation and encoded-byte counts,
16-bit ascending CDF entries including the counter, and the encoded bytes.
Each operation stores operation kind and literal width as bytes, decoded value
as uint32, range and arithmetic value as uint16, consumed bits as uint32, and
the resulting CDF as uint16 entries. Gzip uses a zero modification timestamp.

The normative algorithms are AV1 1.0.0 with Errata 1, sections 8.2.2–8.2.6:
[approved specification](https://aomediacodec.github.io/av1-spec/av1-spec.pdf).
This corpus verifies entropy primitives; it does not establish AV1 picture or
video decoding coverage.

`flat.obu` and `palette.obu` are 16×16, 8-bit monochrome lossless AV1
reduced still pictures encoded by libaom 3.8.2. Their `.gray` files were decoded
independently by libdav1d 1.4.1. The patterns are neutral gray and a repeating
four-color grid. Encoding uses lossless mode, 16×16 fixed partitions, disabled
CDEF/restoration/filter-intra/intra-edge/angle-delta/smooth/Paeth/intrabc, and
zero quantization. The tests require exact pixel equality and complete entropy
termination; this does not establish inter-frame or color AV1 coverage.

The additional monochrome lossless pictures cover black, white, non-neutral
DC residuals, seeded noise, ramps, smoothly varying shapes and palette caches
at 16×16, 64×64, 37×29, 96×80 and 48×40. They use the same independent
libaom/libdav1d versions. The larger pictures allow the encoder to select
partitions and angle deltas, exercising rectangular blocks and partial frame
edges. Nonzero AC coefficients, Golomb escapes, DC sign contexts and inverse
Walsh-Hadamard reconstruction must match every visible source sample.

The `yuv*.obu` samples are Main-profile 8-bit 4:2:0 lossless pictures at
16×16, 32×32, 37×29 and 64×64. Their `.yuv` baselines contain visible planar
Y/U/V samples, decoded independently by libdav1d. They cover independent noisy
color planes, non-neutral DC values, color ramps and chroma correlated with
luma. Encoding permits angle deltas and encoder-selected partitions, disables
palettes for these samples, and retains the preceding filter restrictions.

`sequence.obu` contains five 48×40 Main-profile lossless 4:2:0 key pictures
with ordinary non-reduced sequence/frame headers. The luma and chroma ramps
change between frames; `sequence.reference.yuv` contains all five pictures
independently decoded with libdav1d 1.4.1. Tests also remove repeated sequence
headers and supply the initial sequence through a standard av1C record.

`quantized4.obu` and `quantizedNoise*.obu` exercise lossy Main-profile 4:2:0
four-point DCT, ADST and identity reconstruction. The former uses changing
luma/chroma ramps at 48×40; the latter uses independent seeded color noise at
37×29 with quality settings 4, 12, 30 and 50, covering all four normative
coefficient-CDF initialization groups and both transform sets. All use 4×4
partitions and disabled deblocking, CDEF, restoration, edge filtering,
filter-intra, smooth/Paeth, palette and intrabc tools. The matching `.yuv` files
are independent libdav1d output, not the pre-encoding source pixels.

`quantizedChroma.obu` adds nonzero +2 DC/AC chroma quantizer deltas to the
48×40 lossy ramp case. `transform4.bin.gz` contains 903 two-dimensional inverse
transform cases generated using AOM v3.13.1 `av1_idct4`, `av1_iadst4` and
`av1_iidentity4_c` from
[`av1_inv_txfm1d.c`](https://aomedia.googlesource.com/aom/+/refs/tags/v3.13.1/av1/common/av1_inv_txfm1d.c).
All seven intra transform types have zero, positive/negative basis impulses and
96 seeded coefficient blocks, with every resulting signed residual recorded.
The uncompressed format begins with `AV1TXF04` and a little-endian uint32 case
count; each case has a one-byte transform type and sixteen int32 input values
followed by sixteen int32 output values. Native reference code is used only
when generating this fixture.

`transform1d.bin.gz` records 1,932 independent AOM one-dimensional transform
cases: DCT lengths 4–64, ADST lengths 4–16 and identity lengths 4–32. Cases
include zero input, positive/negative basis impulses and seeded coefficient
vectors. After `AV1TXF1D` and a uint32 count, each record stores type and length
as bytes, followed by the int32 input and output vectors.

`transform2d.bin.gz` composes those native routines according to the normative
two-dimensional scaling, clamping and flipping rules. It contains 772 cases
across all nineteen AV1 transform geometries and applicable transform types.
Cases use zero, DC/AC impulses and seeded coefficient blocks. After `AV1TXF2D`
and a uint32 count, each record stores type, width and height as bytes, followed
by full planar int32 input and output matrices. These transform fixtures verify
primitive arithmetic; they do not establish larger-transform picture coverage.

`hidden.obu` starts from the first ordinary key picture in `sequence.obu`.
Its uncompressed header is rewritten according to the normative hidden-key
syntax (`show_frame=0`, `showable_frame=1`, error resilience and all eight
reference refresh flags), leaving the encoded tile unchanged. A subsequent
frame-header OBU displays reference slot seven. Independent libdav1d decoding
outputs exactly one 48×40 picture, stored as `hidden.yuv`; its samples equal
the original independently decoded key picture. Tests cover combined/separate
access units, reference leases, reset and the prohibition on displaying a
hidden key picture twice through different reference slots.

`interRepeat.obu` contains five independently encoded 48×40 Main-profile 8-bit 4:2:0 lossless pictures: one keyframe followed by four inter frames. It exercises explicit reference indices, 15-bit frame identifiers with 14-bit reference deltas, and refresh masks 2, 4, 8 and 16. The header tests compare these values against the native bitstream trace; inter-picture reconstruction is not covered by these header assertions. The encoder settings disable loop filtering, compound tools, temporal motion fields and global motion.

`interRepeat.yuv`, `interMove.yuv`, `interResidual.yuv`, `interPrimary.yuv` and `interLarge.yuv` are libdav1d reconstructions of five-frame 48×40 Main-profile 8-bit 4:2:0 sequences encoded by AOM. The inputs cover repeated images, two-dimensional translation, newly introduced residual detail, primary-reference CDF loading, and unconstrained lossless partitions. The matching `.obu` files use lossless reconstruction with loop filtering, compound prediction and temporal motion candidates disabled. Every visible sample is compared exactly through both the picture decoder and public codec contracts.

`interQuantized10`, `interQuantized30` and `interQuantized50` are five-frame 48×40 AOM sequences encoded at CRF 10, 30 and 50 with four-point quantized transforms, translation and newly introduced detail. The `.yuv` files are independent libdav1d output. Exact visible-sample comparisons exercise quantized single-reference inter reconstruction, all transform orientations and chroma transform propagation. Optional in-loop filters and compound prediction are disabled.

`interOrdered.obu` is an AOM lossless moving sequence with seven-bit order hints and explicit reference indices. `interShort.obu` replaces only the first inter picture’s explicit indices with normative short signaling; the referenced key picture is shared by all eight slots at that point. Both libdav1d and AOM independently decoded the altered stream and produced exactly `interOrdered.yuv` for all five pictures. The short-signaling tests also cover modular distances, forward/backward ordering and equal-hint slot tie breaking.

`interIntraMixed` contains two lossless AOM pictures with eight-sample partitions and changing two-dimensional patterns. The visible samples are compared against libdav1d. `interCompound` extends the same pattern family to five pictures: its single-reference prefix is verified exactly, then the first selected compound block is rejected explicitly with decoder cleanup and reset checks. Compound permission in a frame header alone does not trigger rejection.

`quantizedTx8`, `quantizedTx16`, `quantizedTx32` and `quantizedTx64` are 48×40
Main-profile 8-bit 4:2:0 reduced still pictures encoded by AOM at CRF 30 with
eight-, sixteen-, thirty-two- and sixty-four-sample partition limits, respectively.
They retain the preceding optional-filter restrictions and use ordinary quantized
transform selection. Their `.yuv` files are independent libdav1d reconstructions.
Tests require every visible sample to match and separately verify that each
encoded picture actually consumes non-four-point transform probability contexts.

`interTx8`, `interTx16`, `interTx32` and `interTxFree` contain five 48×40
Main-profile 8-bit 4:2:0 pictures each. AOM encodes translation and changing
residual detail at CRF 30 with fixed eight-, sixteen-, thirty-two-sample or
unrestricted partitions. Variable transform selection is enabled. In-loop
filters, temporal motion, compound prediction and warped motion are disabled
to isolate transform-tree reconstruction. Every visible sample in the `.yuv`
libdav1d baselines must match through both picture and public codec APIs.

`deblockedTx4`, `deblockedTx8`, `deblockedTx16`, `deblockedTx32`,
`deblockedTx64` and `deblockedSharp7` use ordinary AOM deblocking with
CDEF/restoration disabled. The sharpness case sets sharpness to seven; all
other cases use the default. The independent libdav1d `.yuv` pictures differ
from the matching unfiltered output and must match every managed sample.
`interDeblocked8` and `interDeblockedFree` verify filtered reference propagation
through five-frame moving sequences with fixed or unrestricted partitions.

`loopfilter.bin.gz` contains 2,048 independent AOM v3.13.1 deblocking cases from
[`loopfilter.c`](https://aomedia.googlesource.com/aom/+/refs/tags/v3.13.1/aom_dsp/loopfilter.c).
The corpus covers four-, six-, eight- and fourteen-tap kernels, horizontal and
vertical edges, flat regions, steps, small variations and unconstrained noise.
After `AV1LPF01` and a uint32 case count, each record stores five bytes (tap
kind, direction, limit, boundary limit, high-variance threshold), followed by
256 input and 256 output bytes for a 16×16 block. Gzip has a zero timestamp.
The tests run managed kernels only and compare the entire output block.

`cdefTx8`, `cdefTx32`, `cdefTx64`, `interCdef8` and `interCdefFree` enable
ordinary CDEF on the preceding 48×40 still and five-picture moving sequences.
`cdefRegions` contains three 160×136 4:2:0 pictures with different patterns
across 64×64 regions, exercising multiple strength sets. `cdefMono` is a
117×83 monochrome picture, and `cdefOdd` is a 37×29 4:2:0 picture. All use
independent AOM encoding and libdav1d visible-sample baselines, with restoration
and the other preceding unsupported tools disabled. `cdefMonoZero` sets both
CDEF strength sets to zero without changing index syntax or coded residuals;
`cdefMonoRaw` additionally sets both monochrome deblocking levels to zero.
The independently decoded variants isolate filter stages and rectangular
four-by-sixteen directional edge extension.

`cdef.bin.gz` records 1,024 cases from AOM v3.13.1
[`cdef_block.c`](https://aomedia.googlesource.com/aom/+/refs/tags/v3.13.1/av1/common/cdef_block.c).
Each case verifies native direction/variance and four- or eight-sample filtering
with flat, gradient, edge and noisy inputs, all eight directions, primary/secondary
strengths, damping and frame-edge availability. After `AV1CDEF1` and the uint32
case count, each record stores eight bytes (side, x, y, primary, secondary,
damping, selected direction, detected direction), int32 variance, and 256-byte
input and expected output arrays. Every pixel, including unchanged borders,
is compared exactly. Native reference code is used only for fixture production.

`restorationRegions`, `restorationMono` and `restorationOdd` enable ordinary
restoration on the corresponding CDEF patterns; `restorationMonoNoCdef` disables
CDEF to isolate restoration allocation stages. The moving 160×136 case loads
primary-reference probability snapshots and stores filtered reference pictures.
`restorationWiener` and `restorationWienerRamp` are 256×192 AOM still pictures
with sinusoidal or ramp patterns and native Wiener frame parameters on all
three planes. `restorationMixed` combines smooth noisy and directional textures
at 512×192: all three planes use switchable restoration and 128-sample units.
Matching `.yuv` files contain independent libdav1d reconstruction.

`restoration.bin.gz` records 2,048 native AOM v3.13.1 vectors from
[`restoration.c`](https://aomedia.googlesource.com/aom/+/refs/tags/v3.13.1/av1/common/restoration.c)
and [`convolve.c`](https://aomedia.googlesource.com/aom/+/refs/tags/v3.13.1/av1/common/convolve.c).
There are 1,024 Wiener vectors and 1,024 SGRPROJ vectors spanning all sixteen
SGR sets, coefficient ranges, partial processing blocks, input patterns and frame
edges. After `AV1LRES1` and a uint32 count, each record stores kind, width,
height, x and y as bytes; six signed Wiener coefficient bytes in vertical then
horizontal order; the SGR set byte and two signed projection weights; and
256-byte input and expected output arrays. The tests require exact equality
for every sample and unchanged surrounding pixels. Native routines are used
only to produce the fixture.

`intra.bin.gz` contains 608 smooth, smooth-vertical, smooth-horizontal and Paeth
prediction vectors generated from AOM 3.13.1 C kernels. The stream begins with
`AV1INTR1` and a little-endian 32-bit count. Each record stores byte width, height
and mode; width-plus-one above samples and height-plus-one left samples (corner
at index zero); and width-times-height expected samples. All nineteen transform
geometries use eight constant, random, ramp, extremal and tie patterns.

`smoothRegions` (160×136, three 4:2:0 frames), `smoothMono` (117×83 monochrome)
and `smoothOdd` (37×29 4:2:0) enable smooth/Paeth prediction, quantized residuals,
deblocking, CDEF and restoration. They use libaom 3.8.2 at CRF35 and independent
libdav1d 1.4.1 visible-sample references. Filter-intra and intra-edge filtering
are disabled in these fixtures.

`filter-intra.bin.gz` contains 1,120 recursive intra vectors from AOM 3.13.1 C
kernels. The `AV1FILT1` stream uses the same record layout as `AV1INTR1`, with
filter mode zero through four. Fourteen geometries up to 32 samples use sixteen
edge patterns each. `filterRegions`, `filterMono` and `filterOdd` use the same
source patterns, dimensions and independent encoder/decoder versions as their
`smooth` counterparts, with recursive intra enabled. Edge filtering remains
disabled for these three fixtures.
