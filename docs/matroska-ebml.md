# Matroska reader

`Mediana.Containers.Matroska` provides `MatroskaReader` and
`MatroskaReaderFactory` for one known-length, random-access Matroska Segment.
Register the factory with `MediaEngineBuilder`; supported AVC, AAC and MP3 tracks
use the existing decoder modules. The CLI registers the reader for content-based
probing, complete audio decoding and timestamped video previews.

## Public API and resource bounds

The reader exposes common `ContainerInfo` and immutable native `SegmentInfo`,
`Tracks` and `Attachments` snapshots. Native positive track numbers remain distinct from zero-based
stream indices. Packet counts and payload totals describe the complete index;
nonempty tracks have no inferred decode duration, average bitrate or frame rate.
Declared floating-point duration is converted to nearest-even nanoseconds with
exact binary arithmetic. Native duration and date nanoseconds remain available.
Common calendar dates round to the nearest-even 100-nanosecond tick.

Indexed reads preserve the physical-order cursor. Seeking scans index metadata in
stored decode order, without reading payloads; video sessions validate candidate
access points with their codec before restarting. PTS ordering is not assumed to
match packet order. DTS remains unknown.

`MatroskaReadOptions` bounds the document header, cumulative Info/Tracks payloads
and attachment declarations, index storage, visited elements, tracks, attached
files, text, configurations, blocks and frames. Attachment payload bytes are
excluded from the metadata byte limit and have a separate read size limit.
Each metadata/indexing pass shares its element budget with Segment traversal,
including lookahead needed to find the end of unknown-sized Clusters.
The borrowed source remains open on failure and disposal. Retained packets and
metadata remain valid after reader disposal; canceled packet reads do not advance
the cursor. Overlapping reader operations and disposal are rejected.

EBML and inspected Info/Tracks metadata CRCs are checked. `VerifyMediaChecksums`
also checks Segment, Cluster and attachment CRCs, which reads their complete
covered payloads. Identically
encoded recurring Info/Tracks elements are accepted within the cumulative byte
limit. Differently encoded recurrences are explicitly unsupported.

Unknown codecs, transformed tracks and video presentation settings that cannot
be represented retain their native CodecID, CodecPrivate and
`MatroskaStreamFormats.StoredFrames` representation. This representation does not
claim decoder-ready access units. Cropping, projection, interlaced/stereo/alpha
presentation and incompatible precision/subsampling declarations are not normalized
to the progressive AVC representation. Native scalar Colour declarations retain
CICP codes, range, chroma siting, precision, subsampling and light-level values;
specified color labels and chroma axes resolve over codec labels. Unsupported
registered codes fail interpretation. Mastering and projection payloads are not
fully interpreted, and chapters and Tags are not exposed yet.

CLI preview positions use MP4 movie time, including a supported linear edit list;
Matroska positions use the normalized Segment clock. Stream-copy to MPEG-TS still
requires representable video metadata and explicit PTS/DTS. The reader does not
fabricate decode timestamps to satisfy a writer.

## Attached files

`IContainerAttachmentReader` exposes attached files in storage order, independently
of timed streams and picture tags. `ContainerSession.Attachments` returns the same
declarations; null indicates an unavailable capability, while an empty list means
no attachments. Matroska preserves FileUID, filename, media type and description
under [RFC 9559 section 5.1.6](https://www.rfc-editor.org/rfc/rfc9559.html#section-5.1.6).
The filename remains metadata and is never used to choose an output path.

Opening indexes FileData ranges with bounded header lookahead. Payloads are read
on request into one allocator-owned buffer, with no second payload copy.
`ReadAttachmentAsync` preserves packet cursors; the returned lease survives reader
and session disposal. Sizes use `long`, allowing declarations beyond the maximum
single-buffer read. `MaxAttachments`, `MaxAttachmentBytes`, aggregate metadata/text
limits and the common allocator budget apply independently.

```sh
mediana attachment input.mkv cover.png --index 0
```

This command writes original bytes to the explicit destination and uses the shared
overwrite policy. It does not select cover art, decode an image or execute attached
files. Such selection belongs to the caller. FFmpeg 6.1.1 represents recognized
image attachments as `attached_pic` video streams; Mediana retains native
attachments separately. This distinction follows the
[FFmpeg Matroska demuxer](https://github.com/FFmpeg/FFmpeg/blob/n6.1.1/libavformat/matroskadec.c#L3116-L3154)
and explains differing stream counts for the attached PNG reference.

## Codec configuration and packet ownership

Codec mapping follows the official
[Matroska codec mappings](https://www.matroska.org/technical/codec_specs.html).
AVC retains its AVCDecoderConfigurationRecord and length-prefixed pictures; AAC
retains AudioSpecificConfig and raw data blocks. MP3 has no native initialization;
the common MPEG audio frame-header configuration is derived from the first frame,
and native CodecPrivate is ignored for that mapping. Required Audio/Video masters,
configuration extents and independent per-track and cumulative configuration byte
limits are checked. Configuration snapshots survive source and workspace lifetimes.
Container code depends on Formats and Core contracts, without decoder references.

The packet source borrows its completed index and random-access input. Its own
3,328-byte workspace is reused across lace frames. Each returned packet owns one
payload allocation; an empty payload retains a one-byte backing allocation.
Retained packets survive reader and index disposal. Independent per-track reads
leave the physical-order cursor unchanged, and a failed or canceled read can be
retried without advancing it. Overlapping operations are rejected.

Known codec sample periods establish contiguous lace offsets before a single
nanosecond rounding; otherwise an explicit DefaultDuration can establish them.
Without either fact, later lace PTS values remain unknown. A nonlaced BlockDuration
is converted from Track Ticks. A total laced BlockDuration is not divided into
guessed equal frame durations. DTS remains unknown. MP3 packets must retain their
opening sampling format and contain one complete Layer III frame.

Invisible frames, codec state changes, BlockAdditions and nonzero DiscardPadding
are explicitly rejected by this packet adapter until their required common packet
semantics are implemented. Native index metadata still preserves these declarations.
ContentEncodings, TrackOperation and unmapped codecs retain native stored frames.
They require a compatible decoder/adapter before decoding or normalized stream copy.
Color labels and representable aspect ratios are passed to the common video
contracts; unsupported presentation settings remain native declarations.

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

## Block framing

Block and SimpleBlock framing preserves the native track number, signed 16-bit
relative timestamp and raw flags. Track-number VINTs and lace-size VINTs use
their complete numeric range; an all-one value is not an unknown-size sentinel
in those fields. Reserved flag bits must be zero. SimpleBlock key flags are
retained; a BlockGroup's random-access status requires its ReferenceBlock data.

The reader supports no lacing, Xiph lacing, EBML lacing and fixed-size lacing.
It splits each block into exact source ranges, including all 256 possible laced
frames. Xiph lengths retain their terminating zero when a size is divisible by
255. EBML differences use their width-dependent signed bias. Negative sizes,
inconsistent fixed lengths, truncated headers and frames extending outside the
block are rejected. Frame bytes and header bytes have independent supplied limits.

One reusable 3,328-byte allocation holds a 256-byte input cache and frame ranges.
No complete encoded frame is allocated during framing. The current ranges remain
available until the next read; failure or cancellation makes an incomplete layout
unavailable. Operations, range access and disposal are sequential, and the source
remains borrowed.

The stored block timestamp applies to the first laced frame. Subsequent frames
need track or codec timing as specified by RFC 9559, section 10.3.5. Framing alone
does not assign those timestamps, infer DTS, validate codec payloads or construct
the complete media packet index.

## Segment and track metadata

Info reading preserves the native TimestampScale and floating-point Duration in
Segment Ticks. Dates retain signed nanoseconds relative to 2001-01-01. UUIDs keep
their network byte order, and linked Segment identities remain explicit. These
declarations do not establish a complete packet timeline or coded duration.

Tracks retain declaration order, native number and UID, codec identifier, flags,
language declarations and name. Track timing retains TimestampScale,
DefaultDuration, CodecDelay and SeekPreRoll in their declared units. Native track
numbers do not become public zero-based stream indices at this layer. Unknown
track types and codec identifiers remain explicit values.

Audio and video settings resolve schema defaults after reading the relevant
fields. An empty OutputSamplingFrequency uses SamplingFrequency regardless of
field order. Pixel-unit display dimensions default to dimensions after cropping;
other display units have no implicit dimensions. Invalid crops are rejected
without overflowing unsigned coordinate arithmetic.

CodecPrivate, ContentEncodings, TrackOperation and Projection retain source
extents during structural parsing. Colour scalar fields are parsed within the
same element budget. MasteringMetadata retains a presence flag; its floating-point
payload is not interpreted. CodecPrivate is copied into bounded immutable stream
configuration snapshots when constructing the public reader.

CRCs on metadata masters being read are verified in 4 KiB chunks using IEEE
CRC-32 and little-endian stored values. A present parent CRC reads all covered
bytes, including deferred payloads. CRC must be the first child and contain four
bytes. One metadata reader owns 4,108 media bytes: a 4 KiB scratch buffer and a
12-byte element-header buffer. Longer text uses a temporary quota-backed buffer.
Track count, recognized text bytes and traversed metadata elements have supplied
limits accumulated across that reader's lifetime. Returned strings and metadata
objects are separate from the media-byte quota.

## Native packet index

The index retains physical block order and provides independent per-track packet
lookup. Native track numbers map to metadata declaration order; they need not be
contiguous or begin at one. Laced packets retain their block and frame ordinal,
so random lookup does not require storing a separate payload range per frame.
Per-track packet counts and encoded payload byte totals exclude lace headers.

Each Cluster retains its native unsigned timestamp once. Blocks retain signed
relative timestamps, raw flags and dependency presence. The Timestamp may follow
the blocks; a missing or repeated Timestamp rejects the Cluster. These native
values do not invent DTS, later laced-frame PTS or whole-stream decode duration.
The clock converter below resolves supplied native clocks; the packet reader adds
only offsets and durations established by supported codec or track declarations.

BlockGroup parsing preserves explicit BlockDuration, signed DiscardPadding,
ReferencePriority and deferred CodecState/BlockAdditions extents. ReferenceBlock
values determine dependency presence; a zero reference must be the only reference.
BlockGroup requires exactly one Block and rejects duplicate singleton fields.
A track with DefaultDuration requires BlockDuration in its BlockGroups, as
specified in RFC 9559, section 5.1.3.5.3. Undeclared track numbers and lacing
forbidden by the track reject explicitly.

Allocator-backed pages share an index byte limit. Pages grow to a bounded size
without copying earlier entries, and BlockGroup extensions use a separate sparse
table. Index construction additionally uses 7,448 reusable media bytes. Block
count, per-track packet count, header size, frame size and traversed element count
have supplied limits. Failed or canceled construction releases all index pages;
a completed index retains its pages until disposal and borrows the input source.

Cluster checksum verification is an explicit public read option. CRC
placement and size remain validated in either mode. BlockGroup metadata CRCs are
verified by the metadata reader. Verifying a present Cluster checksum streams all
covered encoded bytes; indexing without that verification avoids the full scan.

## Packet clocks

The internal clock converter evaluates the RFC 9559 block timestamp expression:
`(cluster + relative * track_scale) * timestamp_scale - codec_delay`.
The Cluster timestamp is not multiplied by the track scale. CodecDelay is
subtracted in nanoseconds and negative presentation times remain representable.
DefaultDuration and SeekPreRoll do not change this first-frame timestamp.

Common clock results use nanoseconds. Following RFC 9559, section 11.3, fractional
results round to the nearest nanosecond; Mediana chooses the even integer at a
tie. IEEE 754 track scales are interpreted exactly, without floating-point
arithmetic on timestamps. A supplied contiguous-frame offset retains its exact
media time until the complete expression is rounded once. Repeatedly adding
rounded frame durations would accumulate error and is not used.

The usual unit track scale uses Int128 arithmetic. Other positive finite scales
use an exact BigInteger ratio, including subnormal values. A result outside the
signed 64-bit nanosecond range rejects explicitly. Large native values can cancel
against CodecDelay before that range check; they are not first converted to
double or narrowed to signed 64-bit ticks.

The converter does not infer later laced-frame offsets or synthesize DTS. Known
offsets must come from track or codec facts. Raw BlockDuration values have a
separate conversion using Track Ticks, preserving their distinction from the
nanosecond DefaultDuration and DiscardPadding fields.

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
records, timestamps and decoded hashes. The public engine also decodes the original
MKV through the registered reader and existing AVC/AAC modules.

Block tests check the signed timestamp limits, every track-number VINT width,
all-one numeric VINTs, empty frame ranges, maximum lace counts, eight-byte
differences across cache refills, offsets at the address limit, malformed layouts,
quotas, header/frame limits, cancellation during refill and reader reuse.
All 96 encoded packets across the original MKV and four authored laced AAC
documents have independent size and SHA-256 references. Tests verify every
frame range and stored block timestamp in managed and Native AOT builds.

Metadata checks cover independent declarations and codec facts for all six timed
tracks in those five files, including exact codec-private offsets and hashes.
Shared tests cover defaults, ordering, duplicates, native timestamp units, UUIDs,
cropping, text/count quotas, constructor failures and cancellation during long
text and CRC reads. A virtual one-terabyte CodecPrivate extent without a parent
CRC is indexed with 4,108 media bytes and fewer than sixteen source reads.
Every truncated prefix of the minimum Info payload rejects and releases storage.

Packet-index checks verify every independent packet in physical order and reverse
per-track order, including all payload sizes/hashes and stored first-frame clocks.
Synthetic checks cover 1,500 interleaved blocks, all 256 frames in a block, late
Cluster timestamps, unknown Cluster lengths, shared and global byte limits,
incomplete-index access, malformed BlockGroups and cancellation during a pending
source read. Successful, failed and canceled reads preserve source ownership.

Clock tests compare 1,645 independently computed exact rational expressions,
including delay cancellation, half-nanosecond ties, binary64 scale boundaries and
256 AAC frame offsets at four sample rates. The five real files also verify
common first-frame PTS against independent per-stream FFprobe time bases.
After warmup, 4,096 unit-scale timestamp/duration pairs allocate zero managed
heap bytes on the measured thread in both managed and Native execution.

Public-reader checks cover native/common metadata, unknown codecs, recurring
headers, checksum policy, aggregate limits, unknown-Cluster traversal, cancellation
and concurrent-operation rejection. All 178 packets across six files pass through
the public decode path, covering 137,344 mono audio samples and eight YUV pictures.
Every video picture is selected again through indexed seeking in reverse order.
Movie-time MP4 and Segment-time MKV conversions select the corresponding picture;
complete WAV and PNG outputs match byte for byte on the common fixture.

All 2,800 managed tests and the complete strict Linux x64 Native AOT runner pass.
The Native runner consumes 29 locally built packages without project references.
The standalone Native CLI probes the MKV, exports complete 48 kHz AAC and 8 kHz MP3
audio to PCM16 WAVE, and renders the requested video preview. Independent readback
verifies 77,824 and 47,232 mono output samples, all PNG CRCs and sRGB/pixel-aspect
tags, and every decoded PNG component. The Release build has no warnings or errors;
formatting and license-header checks pass.
