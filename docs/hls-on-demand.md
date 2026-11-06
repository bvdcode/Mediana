# Indexed HLS fragments

`Mp4FragmentHlsSession` generates fMP4 HLS segments from a non-fragmented MP4/MOV
source. Opening reads the container index and required codec configuration, without
remuxing the preceding media. Segment planning uses container sync declarations;
each request inspects its opening AVC access unit before publishing a fragment.
The caller supplies an immutable random-access source, an allocation budget and
destinations. Calls on the same session must not overlap.

```csharp
await using Mp4FragmentHlsSession session = await Mp4FragmentHlsSession.OpenAsync(
    source, allocator, videoStreamIndex: 0, audioStreamIndex: 1,
    cancellationToken: cancellationToken);

string playlist = session.CreateVodPlaylist("init.mp4", number => $"{number}.m4s");
await session.WriteInitializationAsync(initializationDestination, cancellationToken);
await session.WriteSegmentAsync(requestedSegmentNumber, segmentDestination, cancellationToken);
```

The playlist uses HLS version 7 and `EXT-X-MAP`. An application serves the playlist,
initialization and requested fragments independently; creating the playlist does
not create media files. Segment numbers are zero-based. Requests can arrive in any
order and can be repeated. No prior transport-counter snapshots are required.
The session owns its reader, borrows the source and leaves destinations open.
Failed or cancelled output must be discarded; the same session can retry.

Initialization preserves selected sample descriptions, original media time bases,
display matrices, language declarations and edit lists. Sample tables and movie/
track durations are empty, with `mvex/trex` describing subsequent fragments. Each
fragment carries `tfdt`, movie-fragment-relative offsets, sample duration/size/flags
and composition offsets. Encoded sample bytes are copied unchanged. Only the
requested ranges and their opening AVC packet are read; opening inspection currently
reads that one packet separately before fragment writing.

The current session selects AVC video with optional AAC audio. Container sync
declarations must describe non-overlapping presentation windows; a falsely declared
IDR fails when requested. Audio retains its continuous coded timeline rather than
restarting an encoder per segment. The playlist does not declare independent audio
decoding. Source immutability, available random access and the existing MP4 timeline
and codec restrictions apply.

`Mp4FragmentWriter` is the container-layer API for initialization and arbitrary
contiguous sample ranges, separate from HLS boundary planning. It buffers fragment
metadata and one input packet, rather than a complete segment. Header storage counts
against the supplied allocator. It does not decode samples or establish random
access for arbitrary caller-selected ranges.

`Mp4HlsSession` supplies the existing MPEG-TS path. Its preparation pass scans
compressed media to preserve exact transport counters between files. That prepared
TS contract is different from the indexed fMP4 request path. Resetting counters
silently or inserting artificial discontinuities is not required for fMP4 delivery.

The AVC/AAC regression source passes requests in reverse order and repeated final
requests. Managed tests verify cold-opening reads outside every media sample,
requested byte ranges, unchanged payloads and clocks, cancellation, failed reads,
allocator release and operation serialization. FFmpeg independently decodes the
generated fMP4 playlist to exactly the original video's YUV420 pixels and audio's
Float32 PCM bytes.

A Native AOT consumer also passes reverse and repeated segment requests. On a
30-minute AVC/AAC MP4 (17,929,339 bytes), opening reads 586,471 bytes of index and
configuration. Requesting segment 873 of 874 reads a further 19,444 bytes and writes
a 20,022-byte fragment. The allocator retains 586,415 bytes for the session and zero
after disposal. This checks cold I/O on a long timeline; it is not a throughput
comparison with other implementations.

## Encoding requested video windows

`AvcHlsTranscodeSession` encodes a requested video interval using an indexed reader
and a registered video decoder with restart inspection. Creating the session only
validates declarations and builds AVC configuration; it reads no source packets and
encodes no frames. Every request creates fresh codec state, seeks to the preceding
verified restart point, decodes the necessary preroll and encodes the selected
window. The first output picture is an inspected IDR. Previously requested windows
and preceding encoded segment states are not needed.

```csharp
HlsVideoWindow[] windows = indexedWindows
    .Select((window, number) => new HlsVideoWindow(number, window.Start, window.Duration))
    .ToArray();

await using AvcHlsTranscodeSession session = AvcHlsTranscodeSession.Create(
    reader, sourceStreamIndex, decoderFactory, decodedVideoFormat, windows, allocator,
    new H264EncoderOptions { Quantizer = 26 });

string playlist = session.CreateVodPlaylist("init.mp4", number => $"{number}.m4s");
await session.WriteInitializationAsync(initializationDestination, cancellationToken);
await session.WriteSegmentAsync(requestedSegmentNumber, segmentDestination, cancellationToken);
```

Windows are supplied from an index or application timeline. Their numbers must be
contiguous and zero-based, with positive durations and adjacent presentation
intervals. Both boundaries must coincide with source frames; an end inside a frame
fails before any bytes are published. A boundary can fall between source IDRs.
Window clocks need not equal the track clock. Frame timing must be exactly
representable in the output track's time base.

The current encoding session produces one AVC video track at the decoded input
dimensions and preserves the source display matrix. The supplied video format must
match the decoder's output, including color interpretation and sample aspect ratio.
The reader and allocator remain borrowed; operations on either the session or its
reader must not overlap. Requests can be repeated after cancellation or an output
failure. Failed output is discarded by the caller.

Decoded pixels are released as frames are consumed. Encoded packet leases are
retained only for the requested segment so the complete `moof` can precede `mdat`,
without copying packet payloads. Buffer allocations use the supplied allocator.
`Mp4VideoFragmentWriter` exposes new AVC/HEVC track initialization and timed-packet
fragments separately from HLS. Its `avc3`/`hev1` sample entries permit parameter sets
in samples; decoder configuration is also included in initialization. It requires
explicit PTS, DTS, positive durations, sync indications and contiguous decode times.

All 60 independently decoded output pictures from a 12-second AVC source match
reference YUV pixels. Managed checks also compare the encoded packets with sequential
source decoding, including mid-GOP windows, reverse and repeated requests, clocks,
allocator release, cancellation and write/flush failures. These same independent
picture checks run under Native AOT.

On a generated 30-minute AVC file (1,801,168 bytes), opening reads 90,907 bytes of
index and configuration. Requesting the final one-second window reads a further
1,842 bytes, including the preceding GOP's decoder preroll, and writes a 1,331-byte
fragment. The allocator retains 90,851 bytes for the open reader and zero after
disposal. No earlier interval is encoded.

Segment and initialization requirements follow
[RFC 8216 section 3.3](https://www.rfc-editor.org/rfc/rfc8216.html#section-3.3)
and the [ISO BMFF byte-stream format](https://www.w3.org/TR/mse-byte-stream-format-isobmff/).
