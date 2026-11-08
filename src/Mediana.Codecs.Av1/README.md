Managed AV1 reconstruction currently covers Main-profile eight-bit intra and inter pictures with lossless or quantized
transforms, monochrome or 4:2:0, with uniform/nonuniform tile grids inside a
complete FRAME OBU or separate FRAME_HEADER and TILE_GROUP OBUs. The public factory accepts version-one av1C configuration and low-overhead
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

Single-reference lossless and quantized inter pictures support nearest, near, new and global
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

Chroma palette prediction shares the luma palette cache and color-index context
logic. U colors support cached/repeated values; V colors support direct values
and signed wrapped deltas. Both chroma planes share one index map. Palette maps
are allocated on the stack only for blocks using them. Ordinary encoder graphic
pictures, including a default-tool 160×136 key picture and odd 97×81 dimensions,
match independent samples; native palette sizes 2–8 and cached U values agree.

Superblock quantizer and loop-filter deltas support resolution shifts, escaped
magnitudes, sign, bounded accumulation and frame/tile reset. Effective filter
deltas are applied before reference and mode scaling. Three color/monochrome
sequences exercise nonzero positive and negative changes; all nine pictures
match libdav1d, and per-block filter state matches independent AOM inspection.
Temporary filter metadata uses eight bytes per 4×4 block; reference-picture
storage is unchanged.

Temporal motion prediction saves one six-byte vector/distance sample per 8×8
luma region, shared by every reference slot using that picture. Projection
preserves normative source order and block bounds; destination-reference vectors
are computed at lookup, avoiding seven separate projected grids. Temporal stack
weights, precision reduction and global-zero contexts follow the same path as
spatial candidates. Three ten-frame sequences with active reference-motion
selection match every independent output sample. Native projection/position
vectors and scoped failure, cancellation, reset and shared-owner checks pass.

Dual-reference prediction supports the eight compound motion modes, bidirectional
and same-direction reference pairs, average and distance-weighted blending, and
skip mode. Signed intermediate samples retain interpolation precision until the
final blend; only one block-sized prediction is retained. Both motion vectors
and compound contexts fit the existing sixteen-byte motion-grid cell. Independent
AOM block modes, references, vectors and skip/compound flags match six moving
sequences, including lossless content. All decoded samples also match libdav1d,
including a sequence with encoder lookahead enabled.

Masked compound prediction supports wedge masks and difference-weighted masks,
including both signs and rounded 4:2:0 chroma weights. One bounded luma mask is
shared across planes without allocating a frame-sized mask. All 288 wedge
size/index/sign combinations match independent AOM vectors. Six ten-picture
sequences cover actual masked blocks, odd dimensions, variable partitions and
inferred difference masks for large blocks; output samples match libdav1d.

Overlapped motion compensation blends above and left reference predictions in
normative order, with plane-specific geometry, bounded neighbor counts and
rounded masks. It reuses the ordinary interpolation kernel through a borrowed
destination view and a two-kibibyte stack buffer. Motion mode fits the existing
sixteen-byte grid cell. Three moving sequences, including odd dimensions and
hidden reference frames, match every libdav1d sample; 3,492 actual overlap cells
match independent AOM mode inspection for every coded frame.

Local affine warp selects up to eight eligible single-reference neighbors,
fits a bounded fixed-point model and validates its two shear operations.
Normative eight-tap filtering operates in 8×8 regions with a 480-byte stack
intermediate, sharing one model across luma and chroma. No matrix is stored per
motion-grid cell. Independent AOM vectors verify 512 fits and 256 filtered
luma/chroma regions. Three moving sequences, including hidden frames and odd
dimensions, match every libdav1d sample and 156 actual local-warp cells.

Global motion supports identity, translation, rotation/zoom and affine models,
reference-centered parameter syntax and primary-reference inheritance. Seven
models fit fixed-size value state; motion-grid cells do not grow. Spatial and
temporal candidate contexts use the current block's projected global vectors.
Global affine prediction shares the existing warp kernel, including compound
weighted blending; invalid shear geometry uses the normative vector interpolation
path. Independent vectors verify 1,024 global vectors at every block geometry
and precision, plus 192 compound warped regions. A normal camera-pan sequence
matches native pixels and all reference matrices, including selected affine
compound blocks. A compact conformance stream covers signed parameter limits,
all transform types, inherited matrices and keyframe reset.

Inter-intra prediction blends the ordinary clipped inter result with DC, vertical,
horizontal or smooth intra prediction. Smooth masks use the normative size scale;
wedge masks share luma geometry and rounded chroma weights with compound prediction.
One bounded stack prediction is retained, without changing the sixteen-byte motion
grid. Three moving sequences encoded with ordinary libaom tool defaults, including
hidden frames and odd dimensions, match every independent sample. Their 400 actual
inter-intra cells and all other selected motion modes agree with AOM inspection;
88 additional native masks cover every block shape and blend mode.

Segmentation supports all eight feature sets, spatial and temporal ID prediction,
primary-reference feature inheritance, map reuse, dimension resets and disabled
state. Segment quantization, per-component filter deltas, forced references,
skip and identity-global modes share the ordinary block reconstruction path.
Lossless transform selection follows each segment independently of the frame base
quantizer. A budgeted byte per 4×4 luma block stores the map; unchanged maps retain
the previous buffer directly across reference pictures. Motion and filter grid
sizes do not grow. Two cyclic-refresh sequences with nonzero residuals and two
compact conformance sequences match independent decoded samples and per-block
segment IDs, skip flags, references and fixed transform sizes.

Multi-tile reconstruction limits spatial contexts and prediction neighbors to each
tile, while postfilters retain their frame-wide regions. Groups must cover the
complete grid in order. Compressed payloads borrow packet storage, all tiles share
the same frame scratch, and the selected context-update tile runs last to preserve
its probability state without another CDF buffer. Nine native sequences cover
nonuniform and six-tile layouts, separate groups, partial edges, monochrome, large
superblocks and active restoration. See [tile reconstruction](../../docs/av1-tiles.md).

Sequence timing, optional decoder-model delays, operating-point buffer-removal
timestamps and visible/reference-display timestamps are preserved as codec metadata;
container timestamps remain the output clock. See
[timing metadata](../../docs/av1-timing.md).

Quantization matrices support all fifteen weighted levels, the flat level and
independent luma/chroma selection. Immutable embedded weights share native
transform-size aliases without per-frame or per-block allocation. Lossless
segments and identity/one-dimensional transforms bypass weighting. Nineteen
independent moving sequences match every native decoded sample; see
[matrix reconstruction](../../docs/av1-quantization-matrices.md).

Film grain is applied to display storage after reference reconstruction, keeping
reference pixels unchanged. Parameter inheritance, monochrome, clipped geometry
and held-frame ownership are independently verified; see
[film grain](../../docs/av1-film-grain.md).

Super-resolution supports all fixed denominators, per-frame scale changes,
scaled references and restoration in the upscaled domain. Thirteen independent
sequences cover monochrome, odd dimensions, tiles and film grain. See
[super-resolution](../../docs/av1-super-resolution.md).

Intra block copy and high bit depth are not implemented. Encountering these
tools fails explicitly. The module requires explicit factory registration while ordinary AV1 playback
coverage is incomplete.
