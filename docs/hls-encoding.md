The HLS source module can segment encoded AVC video as packets arrive and format
VOD media/master playlists. It depends on Mediana container/core/AVC modules;
encoding and resizing remain separate components. The existing `Mp4HlsSession`
continues to provide indexed AVC/AAC/MP3 stream copy.

## Streaming encoded packets

```csharp
await using AvcHlsWriter writer = AvcHlsWriter.Create(encoder.Stream,
    (number, token) =>
    {
        token.ThrowIfCancellationRequested();
        Stream segment = new FileStream(Path.Combine(outputDirectory, $"segment-{number}.ts"),
            FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
        return ValueTask.FromResult(segment);
    }, allocator, new AvcHlsWriteOptions { TargetDuration = new(6, new(1, 1)) });

foreach (MediaPacket packet in encodedPackets)
{
    await writer.WritePacketAsync(packet, token);
}
await writer.CompleteAsync(token);
string mediaPlaylist = writer.CreateVodPlaylist(number => $"segment-{number}.ts");
HlsVariant variant = writer.CreateVariant("video/index.m3u8");
string masterPlaylist = HlsMasterPlaylist.Create([variant]);
```

Use the `Mediana.Streaming.Hls` namespace and the encoder's configured stream.
Packets must contain valid length-prefixed AVC access units, known PTS equal to
DTS, positive durations and nonoverlapping presentation intervals. Timing must
be exactly representable in the stream clock. The first packet must contain an
IDR; subsequent boundaries use the first IDR at or beyond the requested interval.
Slice-header inspection ignores the container's key-frame flag. It validates
restart headers, rather than reconstructing every residual or proving arbitrary
untrusted bitstreams conformant.

The writer borrows packets and the allocator and owns streams returned by the
destination factory. Streams need only support writing and flushing. Transport
timestamps, continuity counters and PCR continue across segments, with fresh
PAT/PMT and AVC parameters at their starts. `MpegTsWriter.BytesWritten` counts all
successfully written transport packets, including tables and clock overhead.
Only completed segment metadata is retained; media payloads are not indexed or
stored by the HLS writer. `MaximumSegments` is an optional caller-supplied budget.

Calls and disposal must not overlap. Cancellation, invalid input or I/O failure
faults the writer; dispose it and discard partial output. `CompleteAsync` closes
the final destination and is required before either playlist is available. An
empty presentation cannot complete. Completed metadata and playlists remain
available after disposal.

## Variant metadata

Media playlists include floating-point `EXTINF`, a ceiling of the longest
duration as `TARGETDURATION`, VOD completion and `INDEPENDENT-SEGMENTS`. Every
segment starts with a verified AVC IDR and contains video only. Segment intervals
extend to the next boundary; the last ends at the final packet's PTS plus duration.
Source timing gaps are preserved in these intervals.

`CreateVariant` measures complete output. `BANDWIDTH` is the maximum bitrate of
contiguous segment sets whose total duration is between 0.5 and 1.5 times the
playlist's actual `TARGETDURATION`. `AVERAGE-BANDWIDTH` uses every segment's size
and duration. Both include TS overhead and round upward to integer bits/second.
Arithmetic uses exact ticks and integer ratios. A presentation with no eligible
duration window, such as a complete video below 0.5 seconds with target 1, cannot
provide a defined peak; `CreateVariant` rejects it. Its media playlist is still
available. No guessed bitrate is substituted.

The codec identifier takes profile, compatibility and level bytes from the actual
AVC configuration. Optional display dimensions are caller-supplied. The CLI emits
`RESOLUTION` only when known SAR produces an exact integer display width at the
encoded height; otherwise it leaves that optional attribute absent. The encoded
SPS retains the exact SAR in either case.

Master formatting rejects duplicate URIs, invalid or injected URI lines, malformed
AVC identifiers and nonpositive required metadata. Callers supplying multiple
variants are responsible for matching timelines. The CLI checks identical segment
starts and durations before writing its master playlist.

These rules follow [RFC 8216](https://www.rfc-editor.org/rfc/rfc8216.html), sections
4.1, 4.3.3, 4.3.4.2 and 4.3.5.1, and
[RFC 6381](https://www.rfc-editor.org/rfc/rfc6381.html), section 3.3.

## Console workflow and limits

```sh
mediana hls encode input.mp4 output-hls --video-only --segment-ms 6000 --qp 26 --widths 1280 640
```

The CLI reads supported MP4/MOV AVC input once. Each frame passes sequentially to
the reused YUV area plans and fixed-QP AVC encoders for the selected widths.
Frames and pending encoded packets are released as processing proceeds. Each
variant uses a `variant-<requested-width>` directory with `index.m3u8` and numbered
TS segments; the output root contains the master `index.m3u8`. Progress uses the
standard logging options. The parent directory must exist, and the destination
must be new. Files are published together only after every variant completes.

The mode is video-only and requires explicit `--video-only`. It uses one fixed QP
for all variants, with independently coded IDR pictures. It has no target bitrate
or inter-frame compression. Arbitrary display transforms, audio encoding,
combined audio/video transcoding, live/event playlists, fragmented MP4, encryption
and playback scheduling remain outside this path.

## Checks

Complete managed pipelines compare all visible samples in 48 output pictures
against independent decoder references, including resized and non-square-pixel
video. Their 15 segments preserve every AVC packet, presentation time, TS table,
clock and continuity counter. The same verifier is included in the Native AOT
runner. Tests also cover non-seekable outputs, ownership, cancellation, I/O failure,
overlapping calls, false key-frame flags, repeated PTS, segment limits, undefined
short-video peak rates, injected metadata and atomic CLI directory output.

Linux x64 Native AOT runs the complete picture/transport verifier. Standalone
managed and native commands produce identical bytes for all 25 playlist/segment
files in four tested variants. Independent decoding reads each complete playlist
and each of its 18 individual TS segments. It verifies non-square SAR, explicit
audio omission and every reference picture. An in-progress SIGINT returns 130
and removes the temporary variant directories.

A repeated 30-minute 96x80 input produces two variants with 301 segments each.
All 10,802 input pictures survive in both outputs, with every independently decoded
picture, PTS and duration matching its reference. Master bitrates also match an
independent calculation from file sizes and TS presentation times. This small,
repeated source checks complete processing and timing; it does not establish
throughput or compression quality for ordinary HD video.
