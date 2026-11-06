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
Float32 PCM bytes. These checks cover stream copy; independent on-demand encoding
is a separate operation.

Segment and initialization requirements follow
[RFC 8216 section 3.3](https://www.rfc-editor.org/rfc/rfc8216.html#section-3.3)
and the [ISO BMFF byte-stream format](https://www.w3.org/TR/mse-byte-stream-format-isobmff/).
