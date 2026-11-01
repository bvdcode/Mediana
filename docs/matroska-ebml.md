# Matroska EBML foundation

`Mediana.Containers.Matroska` contains internal EBML value decoding, document
header validation and bounded Segment traversal. Track descriptions, packet reading,
seeking and container attachment extraction are the next integration work.
MKV probing and decoding are not exposed through the public engine yet.

## Format rules

The implementation follows [RFC 8794](https://www.rfc-editor.org/rfc/rfc8794),
with the Element ID update in [RFC 9559, section 4.2](https://www.rfc-editor.org/rfc/rfc9559#section-4.2).
Matroska's one-byte ID `0x80` is valid. IDs retain their marker, use the shortest
valid encoding and reject reserved all-one values. Data sizes accept padded
encodings and represent the all-one sentinel as an unknown extent. Schema-level
parsing must resolve where an unknown-sized master ends.

Unsigned and signed values accept zero through eight bytes. Empty numeric
values use an explicit schema default supplied by the caller. Four- and
eight-byte IEEE floating-point values preserve signed zero and nonfinite values;
field-specific range restrictions belong to the corresponding schema reader.
String values require printable ASCII or valid UTF-8 before their first null
terminator. Following bytes are ignored as specified in RFC 8794, section 13.

The document header applies schema defaults, rejects duplicate scalar fields
and requires `DocType` to be `matroska`. Minimum reader versions must be supported
and cannot exceed their writer versions. A higher writer version alone does not
reject a file with a supported minimum reader version. Known-size unknown fields
are skipped; document-type extensions are explicitly unsupported.

`EBMLMaxIDLength` must be four and `EBMLMaxSizeLength` must be between one and
eight. Those declarations apply to the body, rather than restricting the header's
own size encodings. An optional header CRC must appear first, contain exactly
four bytes and match the remaining header payload using IEEE CRC-32 with
little-endian storage. PNG and Matroska share the same checksum calculation;
each format retains its own byte order and checksum coverage.

## Segment boundaries

The structure reader requires a known-length random-access source. It skips
bounded root-level padding, reads the Segment and returns its children as
offset/length descriptors. Only Segment and Cluster may have unknown lengths.
An unknown-sized Cluster ends at the next recognized schema sibling, its parent
boundary or end of input. Globals remain within the Cluster and do not terminate
it. Block payloads are skipped by their declared extents, so bytes resembling
element IDs inside a block are not interpreted as structural boundaries.

Unsupported concatenated Matroska documents are rejected rather than silently
dropping later documents. Structural traversal establishes extents; it does not
validate packet contents, all body CRCs or track-level requirements.

## Bounds and ownership

The element reader borrows a random-access source and leaves its sequential
cursor untouched. Its reusable 12-byte header buffer is charged to the common
media allocator and released on disposal. It reads within the supplied parent
boundary and known input length, validates fragmented read counts and checks
header and payload extents before constructing an element. A large declared
payload does not cause a payload allocation.

Document-header payload allocation is bounded by a supplied byte limit. The
12-byte scratch buffer is released before allocating that payload. Successful,
failed and canceled header reads release their media bytes and leave the source
open. Segment traversal reuses twelve media bytes regardless of declared payload
size, including a tested virtual one-terabyte extent.

The buffer quota covers media bytes; object and asynchronous I/O overhead are
separate. Header reading does not inspect an entire payload or establish its
codec validity. Operations and disposal are sequential.

## Verification

The tests cover all eight VINT widths, reserved/canonical ID boundaries, padded
sizes, unknown extents, 2,048 signed/unsigned input patterns and normative string
termination. Reader tests cover every incomplete prefix of a maximum-width
header, nested boundaries, offsets at the address limit, oversized declarations,
invalid source counts, cancellation during an asynchronous read, reuse, quota
rejection and independent source lifetime.

Header tests cover defaults, empty numeric values, duplicates, version ordering,
supported minimum versions, padded size encodings, unknown fields, CRC byte order,
truncation and cancellation during both header-prefix and payload reads. Segment
tests exercise all eight recognized sibling types, known/unknown parents, globals,
embedded false IDs, invalid unknown extents, trailing data and cancellation/reuse.

An independently muxed AVC/AAC file with a PNG attachment provides exact header
facts and all nine Segment child extents. The same descriptors are checked after
changing its Segment and both Clusters to unknown-size encodings. Independent
full decoding of those two fixture variants preserves all 84 video/audio frame
records, timestamps and decoded hashes. This validates the fixtures; public MKV
media decoding is still pending.

All 2,745 managed tests and the complete strict Linux x64 Native AOT runner pass.
The Release build, formatting and license-header checks also pass.
