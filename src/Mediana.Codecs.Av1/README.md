Managed AV1 reconstruction currently covers Main-profile eight-bit key pictures with lossless or quantized
transforms, monochrome or 4:2:0, with a single tile inside a complete frame
OBU. The public factory accepts version-one av1C configuration and low-overhead
OBUs with explicit sizes or an implicit final OBU extent. Hidden key pictures, eight reference slots and
show-existing display are supported, including multiple hidden/visible frame
OBUs in one access unit. Reference display retains the existing pixel buffer;
reset and disposal release references independently of returned frames.
Output keeps native planar samples, packet timing,
render aspect ratio and codec color labels without copying pixel storage.

Verified reconstruction includes all partition shapes, palette luma,
nonzero lossless coefficients, directional intra prediction with filtered and upsampled edges, chroma prediction from luma, ordinary multi-frame sequence headers,
odd dimensions and partial frame edges. Quantized four-point reconstruction
includes DCT, ADST, identity and horizontal/vertical DCT transform types, all
four coefficient probability groups, chroma quantizer deltas and reduced
transform sets. Independent libdav1d baselines require
exact equality for every visible sample. Entropy fixtures separately check
arithmetic state and CDF adaptation against AOM.

Quantized intra reconstruction shares coefficient, dequantization and prediction
logic across square and rectangular transforms up to 64 samples. Four native
48×40 still pictures with eight-, sixteen-, thirty-two- and sixty-four-sample
partition choices consume larger transform contexts and match every visible
libdav1d sample, including clipped edge blocks and chroma transforms.

Single-reference lossless and quantized inter pictures support nearest, near, new and identity-global
motion, primary-reference probability loading, frame identifiers and expiry, modular
order hints and short reference signaling, native
eighth-sample luma and sixteenth-sample chroma interpolation, sub-eight-pixel
chroma motion and reference scaling. Motion metadata and stored probability
snapshots are accounted against the caller buffer budget. Independent moving
sequences with nonzero residuals and unconstrained lossless partitions match
every native reference sample.

Four-point inter transforms include all sixteen DCT, ADST, flipped ADST and
identity combinations, with native chroma transform propagation. Three
independently encoded lossy sequences at different quantization levels match
every libdav1d sample. Variable transform trees share the same coefficient and
prediction paths; four additional five-frame sequences with eight-, sixteen-,
thirty-two-sample and unrestricted partition choices match exactly. Transform
size metadata uses the existing motion-grid allocation. Frame permissions for compound prediction, skip mode,
inter-intra blending and switchable motion are parsed independently of the
selected block tool; ordinary single-reference translation remains supported.

Deblocking applies the normative four-, six-, eight- and fourteen-tap filters
before reference storage. It preserves reference/mode delta state across
primary-reference loading, supports luma/chroma levels and sharpness, and uses
four bytes of edge metadata per luma 4×4 block only when filtering is enabled.
It modifies the original pixel buffer in place. Six native still pictures and
two moving sequences match independent filtered output exactly; 2,048 AOM
kernel vectors cover every tap size in both directions.

CDEF applies normative direction detection and primary/secondary constrained
filtering before reference storage. It keeps twelve luma rows and eight rows
per chroma plane, totaling twenty bytes per padded luma column for 4:2:0,
without a second image buffer. Eight strength sets, skipped blocks, monochrome,
partial frame edges and multiple 64×64 strength regions are supported. Native
kernel vectors and independently decoded still/moving pictures match exactly.
Rectangular directional prediction limits extended edges to twice their own
width or height, including narrow four-by-sixteen transforms.

Loop restoration reads per-unit Wiener and self-guided projection parameters
with reference-centered arithmetic syntax and adaptive probability state. Wiener
uses separable seven-tap luma or five-tap chroma filtering; self-guided projection
supports all sixteen radius/strength sets. Horizontal and vertical rolling sums
share local-window statistics across each processing strip. Filtering reuses a
row buffer of at most ten luma rows and retains only four deblocked rows per
internal 64-line stripe boundary before CDEF. Real Wiener, SGRPROJ and switchable
regions, moving references and monochrome/partial edges match independent native
output; 2,048 additional kernel vectors cover every SGR set and Wiener extremes.

Smooth, smooth-horizontal, smooth-vertical and Paeth intra prediction apply to
luma and chroma. Their transform selection follows the normative UV mode mapping.
AOM kernel vectors cover all nineteen transform geometries, including clipped
destinations, constant inputs, extrema and tie cases; three independently
encoded still/moving fixtures match every native output sample.

Recursive filter-intra supports all five modes, adaptive arithmetic
mode selection and the normative luma-transform probability contexts. Prediction
keeps three rows of at most 32 samples instead of a full transform buffer.
AOM kernel vectors exercise every supported square/rectangular transform and
cropped destinations; independently decoded still/moving sequences verify
residuals and in-loop filtering together.

Directional prediction filters reference edges with normative strength, corner
filtering and upsampling for every angle delta. Smooth-neighbor state shares
the existing per-plane decoded-block byte, without an extra picture-sized
allocation. Independent native vectors cover all geometries, filter types,
availability and partial destinations. Ordinary default-tool libaom stills and
filtered moving sequences match independent decoded samples.

Temporal motion prediction saves one six-byte vector/distance sample per 8×8
luma region, shared by every reference slot using that picture. Projection
preserves normative source order and block bounds; destination-reference vectors
are computed at lookup, avoiding seven separate projected grids. Temporal stack
weights, precision reduction and global-zero contexts follow the same path as
spatial candidates. Three ten-frame sequences with active reference-motion
selection match every independent output sample. Native projection/position
vectors and scoped failure, cancellation, reset and shared-owner checks pass.

Compound prediction,
non-identity global motion, overlapped/warped reconstruction,
intra block copy,
chroma palette, segmentation, multiple
tiles, separate frame-header/tile-group OBUs, super-resolution, high bit depth,
film grain and decoder-model timing are not implemented. Encountering these
tools fails explicitly. The module is not registered in the default pipeline
or published as a package while ordinary AV1 playback coverage is incomplete.
