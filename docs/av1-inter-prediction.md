# AV1 inter prediction precision

Motion compensation, affine warp and compound blending share typed sample
storage: byte samples at eight bits and unsigned sixteen-bit samples at ten
or twelve bits. Strides and plane offsets count samples. Reference leases are
read directly, without copying or converting a complete reference picture.

The prediction rounding depends on sample precision:

| Precision | Horizontal shift | Single-reference vertical shift | Compound vertical shift | Compound final shift |
| --- | --- | --- | --- | --- |
| 8 bits | 3 | 11 | 7 | 4 |
| 10 bits | 3 | 11 | 7 | 4 |
| 12 bits | 5 | 9 | 7 | 2 |

Compound scratch occupies two bytes per sample at every precision. Eight-bit
prediction preserves its signed sixteen-bit representation. Higher precision
uses an unsigned representation with a fixed rounding offset; sharp filters
can produce both negative intermediate values and values above 32,767 before
final clipping. Difference masks account for both the sample depth and the
remaining compound precision.

The arithmetic is checked against the independent AOM v3.13.1 implementations
of [convolution](https://aomedia.googlesource.com/aom/+/refs/tags/v3.13.1/av1/common/convolve.c),
[affine warp](https://aomedia.googlesource.com/aom/+/refs/tags/v3.13.1/av1/common/warped_motion.c)
and [compound masks](https://aomedia.googlesource.com/aom/+/refs/tags/v3.13.1/av1/common/reconinter.c).
The regression corpus contains 1,890 cases and 313,152 expected output samples,
covering all three precisions, luma and chroma, scaled and unscaled prediction,
four- and eight-tap filters, affine warp, weighted and masked compound prediction,
and sharp-filter extrema. It checks complete output regions, unchanged surrounding
samples, masks and reference storage.

Every expected sample matches in ordinary JIT, disabled-intrinsics JIT and
portable Native AOT builds. Warmed kernel calls allocate zero managed bytes in
checked allocation intervals with a positive allocation control. These checks
verify the prediction kernels; complete stream coverage is documented separately.
