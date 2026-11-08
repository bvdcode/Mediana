# AV1 tile reconstruction

The AV1 public decoder accepts complete FRAME OBUs and complete coded pictures
formed from a separate FRAME_HEADER followed by one or more TILE_GROUP OBUs.
Uniform and nonuniform grids support up to 64 tile rows and 64 tile columns,
64×64 or 128×128 superblocks, partial frame edges and monochrome Main-profile
8-bit pictures. The packet contract remains an access unit containing at most
one visible picture, with its hidden reference pictures. Container timestamps
and durations retain priority over codec timing metadata.

The parser validates tile indices, ordered complete group coverage, zero
alignment, checked little-endian tile extents and the selected context-update
tile. FRAME OBUs require a whole-grid group. A truncated or inconsistent group
fails explicitly; reset releases references and permits fresh input.

Each tile starts from the same initial probability state and has independent
spatial contexts, quantizer deltas and restoration references. Spatial motion
candidates, intra edges, overlap prediction and segment contexts obey tile
boundaries. Motion-vector clamping still uses frame geometry. Deblocking,
directional enhancement and loop restoration run after all tiles have been
reconstructed and retain their normative frame-wide filtering regions.

Compressed payloads are borrowed from the input packet. All tiles share one
reconstructed frame and the existing frame scratch buffers. The selected
context-update tile is decoded last, retaining its final CDFs without a second
CDF buffer. Single-tile pictures allocate no managed tile-grid arrays; multiple tiles
use two bounded arrays of row and column starts.

Nine independently encoded sequences cover horizontal/vertical grids, odd
sizes, monochrome, 128×128 superblocks, nonuniform grids, six-tile groups and
active restoration. Every visible picture matches stock libdav1d 1.4.1 by
SHA-256 across all native visible samples. Normal and scalar managed runs and
a strict Native AOT client pass, including reset, packet timing, random-access
inspection and buffer release. Malformed-group tests cover missing, duplicated
and reordered groups, missing headers, alignment, truncation, cancellation and
caller-selected memory budgets.

The normative syntax and decoding rules are AV1 1.0.0 with Errata 1,
sections 5.9.15, 5.11, 6.8.14, 6.10.1 and 7.7 of the
[approved specification](https://aomediacodec.github.io/av1-spec/av1-spec.pdf).
Fixture generation and independent decoder provenance are documented with the
[AV1 test data](../tests/Mediana.Codecs.Av1.Tests/Data/README.md).
