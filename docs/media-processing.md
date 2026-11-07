# Selected audio and video processing

`MediaEngine.ProcessAsync` coordinates explicit copied and encoded tracks from an
indexed container. `OpenProcessingAsync` exposes the same operation for a caller
that already owns an indexed reader and a container writer. Codec factories carry
their own settings, independent of the destination container or command line.

```csharp
MediaTrackRequest[] tracks =
[
    new VideoEncodingRequest(videoIndex, 0, new H264EncoderFactory(videoOptions),
        new VideoPreparationOptions { MaximumWidth = 1280, MaximumHeight = 720 }),
    new AudioEncodingRequest(audioIndex, 1, new AacEncoderFactory(audioOptions))
];

MediaProcessingResult result = await engine.ProcessAsync(source, destination,
    "mpegts", tracks, allocator, cancellationToken: cancellationToken);
```

The engine needs registered input readers and decoders, plus a writer for the
requested output identifier. Per-track encoder factories are explicit, so two
output tracks may use different settings. Only selected tracks are processed.
Input and output stream indices are identities, independent of their list positions.
Output indices must be unique. `PacketCopyRequest` preserves compressed payload
and timestamps while changing the destination track identity without copying bytes.

## Presentation intervals

`MediaProcessingOptions.Selection` specifies an exact half-open interval on the
input reader's presentation timeline. No implicit timestamp rebasing occurs.
Indexed decoding seeks to a codec-validated restart or required codec preroll.
It reads the selected region and necessary decoder warmup, without reading all
preceding media. The optional read limits are cumulative across selected tracks,
including inspection, discarded frames and EOF checks.

Encoded audio selects PCM sample instants in the interval, rounding both bounds
toward the next input sample. Encoded video selects frames whose starts lie in
the interval. Known display durations are clipped at overlapping subsequent frame
starts and the selected end; shorter durations preserve gaps. No missing frames,
samples or durations are synthesized. Rotation, sample aspect ratio and explicit
pixel precision conversion use shared `VideoPreparation`.
`VideoIntervalAlignment.CompleteFrames` instead requires a contiguous interval
whose start and end both coincide with complete presentation frames. It rejects
partial boundaries and gaps; on-demand HLS consumes this same mode.

`IContainerPresentationReader` exposes explicit presentation selections without
media reads. MP4 interprets one unit-rate media edit, optionally preceded by an
empty delay edit, on the reader's selected packet timeline. These declarations
intersect requested encoding intervals. Absence of an explicit selection remains
distinct from an empty selection. Nonlinear edits are rejected.

Before applying the requested interval, video intersects each decoded source
display extent with the container's presentation range. A picture crossing the
declared start retains its pixels and receives the trimmed start and duration;
the closing picture is clipped at the declared end. This follows Core Media's
[trimmed presentation timestamp](https://developer.apple.com/documentation/coremedia/cmreadysamplebuffer/outputpresentationtimestamp)
model. Source clipping does not relax requested `CompleteFrames` boundaries or
fill gaps. Automatic video and HLS planning use the same source extents, exclude
decode-only preroll and postroll, and retain a known earlier media end rather
than extending the final picture to a longer declaration. Planning and exact
clock queries read indexed timing metadata without encoded payload.

The [QuickTime edit-list specification](https://developer.apple.com/documentation/quicktime-file-format/edit_list_atom/edit_list_table)
defines separate movie-duration and media-start units. The operation does not
infer additional undocumented codec delay from container names.

Compressed interval copy requires exact packet boundaries and a contiguous range
in decode order. Video boundaries additionally require codec-inspected independent
access packets at the beginning and the next boundary, unless the interval reaches
EOF. Misaligned copy requests fail without enlarging the range or re-encoding it.
`PacketCopyAlignment.PresentationStarts` explicitly assigns each compressed packet
to the interval containing its presentation start. Adjacent intervals then own
each audio packet once, including packets that cross a boundary. Empty packet
selections are allowed. Video still requires independent decode boundaries.
`PacketCopyRequest.Selection` can set an explicit per-track interval instead of
the operation interval, for example to include initial audio preroll.
Whole-track copy retains every encoded sample; container edit trimming is an
encoding operation and is not silently applied to compressed packets.

## Clocks, delay and ownership

The packet merge holds at most one pending encoded packet per track and orders
known decode timestamps across different exact clocks. Video clock derivation
walks indexed metadata with constant workspace. An explicit
`VideoEncodingRequest.OutputTimeBase` can reuse a previously planned exact clock;
timestamps and durations must fit it without rounding.
An explicit clock also supports indexed readers that do not expose metadata-only
packet clock queries; unsupported automatic selection fails before payload reads.

The H.264 encoder checks level cadence against the timestamps of consecutive
pictures, independently of their display extents. A clipped final picture can
therefore last 1 ms without implying a 1,000 fps stream. Positive, exact display
durations and nonoverlapping timestamps remain required. This follows
[H.264 (June 2026), Annex A.3.1 and Annex C.2.2](https://www.itu.int/rec/T-REC-H.264-202606-I/en),
which define the relevant intervals between successive pictures and exclude the
last displayed picture from the next-picture interval constraint.

`MediaTrackResult` reports encoded frames, input PCM samples, packet/payload counts
and available encoder priming/padding. Sample counts describe PCM submitted to the
encoder. Compressed output also contains the encoder's declared delay and padding;
they are not automatically removed by every container. MPEG-TS does not provide
sample-accurate gapless trimming, so a standalone decoded AAC track may include
these additional samples. A muxer with explicit presentation edits must use the
reported timing when exact trimmed playback is required.

The processing session owns its codec and preparation state and borrows the
indexed reader and allocator. The caller serializes use of that reader. Writing
consumes the session once, borrows the writer, and completes it on success.
The optional packet observer runs after each successful writer call and borrows
the packet only for that callback. Retain a packet explicitly to extend its lifetime.
Observer failures or cancellation abort without completing the writer.
Dispose the session after success, cancellation or failure. Other track resources
are released even when individual disposal fails. Failed container output must be
discarded. `ProcessAsync` also owns its opened container and writer, while borrowing
the supplied byte source and destination stream.

`TranscodeVideoAsync` is a single-video convenience adapter over this same indexed
operation. It retains explicit audio omission and progress reporting while sharing
exact output clocks, presentation selection and preparation across source changes.
Custom sequential readers can use `MediaOperations.EncodeVideoAsync` with their
own frame callback, encoder and exact output clock.

Sequential-only demuxers are supported by the existing decoding APIs. Independent
multi-track processing currently requires indexed packets. On-demand AVC HLS uses
this operation with optional original AAC packets on their unchanged audio grid.
`AacEncodingInterval` and `AacIntervalEncoderFactory` supply a shared packet grid,
bounded analysis context and explicit priming/padding for independently requested
AAC intervals. The [on-demand HLS API](hls-on-demand.md#encoding-aac-audio) uses
those contracts alongside video and applies an explicit presentation endpoint.

## Coded packets and displayed pictures

`IIndexedVideoPresentationReader` exposes displayed-picture metadata separately
from compressed packets. `ContainerVideoPresentationUnit` identifies the source
packet, output ordinal, presentation timestamp and known display duration.
A decode-only reference packet contributes no displayed unit; a representation
with several independently timed pictures can expose several units. Unknown
clocks remain unknown. Restart and packet cursors still refer to coded packets.

`VideoSegmentPlanner` consumes this index when available. The packet-clock path
requires one displayed unit per indexed packet. Both paths use caller-budgeted
workspace and perform no encoded reads or frame decoding during planning.
A codec or container with several displayed pictures but no distinct timing for
them cannot claim exact presentation indexing by dividing a nominal duration.

Matroska resolves implicit video display extents to the next visible timestamp.
Explicit `BlockDuration` retains its declared extent, including real gaps.
A known final explicit or nominal duration supplies the tail; an unknown tail
stays unknown. Hidden VP8/VP9 reference frames do not create presentation cuts.
Container invisible flags and bounded shared codec prefix syntax are checked
while indexing. The nominal `DefaultDuration` remains part of raw packet metadata;
the separate display-extent index describes how long a visible picture is held
until the next PTS. This is an explicit presentation interpretation, rather than
a claim that the container specification discards its declared default duration.
See [Matroska timing and block semantics](https://www.rfc-editor.org/rfc/rfc9559.html#section-5.1.3.5.3)
and [WebM alternate-reference guidance](https://www.webmproject.org/docs/container/). Raw packet clocks and durations remain inspectable unchanged.
The existing `IIndexedPresentationTimingReader` hook supplies resolved durations
to the same decoding pipeline used by file operations and requested HLS windows.

## Encoded MP4 fragments

`Mp4EncodedFragmentWriter` writes shared AVC/HEVC video and AAC-LC audio initialization
and media fragments. `Mp4VideoTrackDefinition` and `Mp4AudioTrackDefinition` carry
separate stream and MP4 track identities. `Mp4EncodedFragmentPacketWriter` implements
the same container-writer contract consumed by `MediaProcessingSession`; it retains
only the current fragment's compressed packet leases until completion/disposal.
Packets are grouped by track in `mdat`, with matching relative `trun` offsets.
Empty audio selections omit that fragment's audio `traf` while retaining its
initialization declaration.

An explicit track `TimestampOffset`, measured on its unit-numerator MP4 clock,
permits negative codec preroll in the input packets. Stored decode and presentation
times receive that offset; a unit-rate initialization edit maps presentation back.
No input packet or payload is changed. For example, 1024 priming samples at 48 kHz
use an offset of 1024 on a 1/48000 track clock. The zero-duration initialization
edit describes a continuing fragmented presentation, as defined by ISO BMFF and
the [MSE ISO BMFF initialization requirements](https://www.w3.org/TR/mse-byte-stream-format-isobmff/#iso-init-segments).
The AAC sample entry currently accepts indexed sample rates through 64 kHz;
88.2/96 kHz sample entries require an additional representation.

Independent FFmpeg verification of an encoded AVC/AAC fragment recognizes both
tracks, applies the initial AAC skip of 1024 samples, and decodes exactly 76,800
audio samples from 76 access units. Packet and initialization tests cover shared
headers, signed composition offsets, explicit preroll, ownership and failure before
header publication for invalid timing.

## Verification

Managed tests exercise whole audiovisual encoding, video copy with audio encoding,
noncontiguous track identities, fractional interval bounds, exact PCM counts,
encoder delay/padding, timestamp-ordered muxing, shared read limits and source
ownership. Repeated cold requests for the final second of a 12-second reordered
AVC/AAC source encode five pictures and 48,000 PCM samples while reading only the
needed source region. Cancellation, output errors and disposal release all track
buffers. Linux Native AOT runs the scaled audiovisual interval and compares repeated
complete MPEG-TS outputs. Independent FFmpeg decoding accepts both output tracks.
