The HLS source module can segment encoded AVC video as packets arrive and format
VOD media/master playlists, including optional AAC-LC or MPEG-1/2 Layer III
audio. The CLI can copy the original audio or encode it with the AAC source module.
The HLS module depends on Mediana container/core/AVC modules;
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
be exactly representable in the stream clock. The first video packet must contain
an IDR; subsequent boundaries use the first IDR at or beyond the requested interval.
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

For audiovisual output, use the `Create(videoStream, audioStream, factory,
allocator, options)` overload. Stream indices must differ. Both tracks require
known PTS equal to DTS, positive durations and nonoverlapping per-track intervals.
Submit packets in globally increasing DTS order, with video before audio at equal
DTS. An audio packet starting before a cut stays in the preceding segment even
when its duration crosses the boundary. Every packet is written once. Initial
audio priming may precede the first video packet and is retained. The playlist
starts at the first video PTS; its final duration includes any later audio tail.
Both configured tracks must contain packets before completion.

The common audio/video clock uses an exact denominator; unrepresentable clocks
are rejected before opening any destination. Transport timestamps use the TS
90 kHz clock and configured offset with nearest-even rounding. Audio
does not claim independent segment decoding: AAC overlap and MP3 reservoir state
can depend on earlier packets. Continuous playlist decoding retains that state.

## Variant metadata

Media playlists include floating-point `EXTINF`, a ceiling of the longest
duration as `TARGETDURATION` and VOD completion. Video-only output also declares
`INDEPENDENT-SEGMENTS`. Every segment's video starts with a verified AVC IDR.
Segment intervals extend to the next video boundary; the last ends at the later
of the final video/audio PTS plus duration.
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
AVC configuration. Audio adds `mp4a.40.2` for AAC-LC, `mp4a.6B` for MPEG-1 Layer III,
or `mp4a.69` for MPEG-2 Layer III. Optional display dimensions are caller-supplied. The CLI emits
`RESOLUTION` only when known SAR produces an exact integer display width at the
encoded height; otherwise it leaves that optional attribute absent. The encoded
SPS retains the exact SAR in either case.

Master formatting rejects duplicate URIs, invalid or injected URI lines, malformed
AVC identifiers and nonpositive required metadata. Callers supplying multiple
variants are responsible for matching timelines. The CLI checks identical segment
starts and durations before writing its master playlist.

These rules follow [RFC 8216](https://www.rfc-editor.org/rfc/rfc8216.html), sections
4.1, 4.3.3, 4.3.4.2 and 4.3.5.1, and
[RFC 6381](https://www.rfc-editor.org/rfc/rfc6381.html), section 3.3, with audio
object types from the [MP4 Registration Authority](https://mp4ra.org/registered-types/object-types).

## Console workflow and limits

```sh
mediana hls encode input.mp4 output-hls --video-only --segment-ms 6000 --qp 26 --widths 1280 640
mediana hls encode input.mp4 output-av --audio-stream 1 --segment-ms 6000 --qp 26 --widths 1280 640
mediana hls encode input.mp4 output-aac --audio-stream 1 --audio-codec Aac --audio-bit-rate 128000 --widths 1280 640
```

The CLI decodes supported MP4/MOV AVC input once. Each frame passes sequentially to
the reused YUV area plans and fixed-QP AVC encoders for the selected widths.
Frames and pending encoded packets are released as processing proceeds. Each
variant uses a `variant-<requested-width>` directory with `index.m3u8` and numbered
TS segments; the output root contains the master `index.m3u8`. Progress uses the
standard logging options. The parent directory must exist, and the destination
must be new. Files are published together only after every variant completes.

Choose either `--video-only` or `--audio-stream`; they are mutually exclusive.
Without `--audio-codec`, audio copy uses an additional indexed reader over the same
random-access source and copies each selected audio packet to all variants without decoding it. Video
decoding still reads the container's sequential packets. Audio is read again for
copy; no whole-file payload queue is retained. Input edit timing uses the linear
movie timeline for both readers. Unsupported, empty or missing audio tracks fail.

With `--audio-codec Aac`, a separate audio decoder reads the selected track in
media time. Existing PCM sample selection applies the explicit MP4 edit list:
one unit-rate media edit, optionally preceded by one empty edit. Samples before
the edit are decoded for codec state and discarded; the exact selected end clips
the source tail. The movie origin preserves a leading empty-edit delay, including
origins between sample instants. With no edits, all decoded samples are retained.
Nonlinear edits, gaps, unavailable sample ranges and unsupported formats fail.
No resampling, downmix or silent PCM insertion is implicit.

The AAC encoder uses the selected rate/channel order, one shared audio bit ceiling
for all variants and exactly the same encoded audio packets in each variant.
`--audio-bit-rate` requires `--audio-codec Aac`; its default is 128000 raw bits/second.
It is a variable bitrate ceiling excluding transport overhead, rather than a target
file size. The source's explicit edit removes its declared codec delay; newly
encoded AAC introduces its own 1024-sample priming. That packet starts before the
selected PCM origin. Final drain emits the overlap tail and at most 1023 padding
samples; TS retains them without gapless trimming metadata. No priming is repeated
at segment boundaries. The playlist's final duration includes the encoded tail.

Audio decoding/encoding happens once for all widths. Packet delivery keeps one
lookahead packet and writes video before audio at equal DTS. PCM conversion,
trimming and encoder working storage use bounded frame buffers; the recording is
not collected in memory. Decoders still read their own sequential container data.
The command reports input PCM count and final AAC priming/padding at completion.
[AAC quality and rate limits](aac-encoding.md) apply to this path too.

The mode uses one fixed QP for all variants, with independently coded IDR pictures.
It has no target video bitrate or inter-frame compression. Per-variant audio rates,
arbitrary display transforms, live/event playlists, fragmented MP4, encryption
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

Audiovisual checks retain every AAC/MP3 packet, PTS/DTS and transport counter
alongside newly encoded AVC video in managed and Native AOT execution. Six short
variants contain 160 pictures across 54 segments. Independent decoding reads
every playlist and every segment's video; complete audio elementary bytes and
decoded PCM match the original audio, and packet times retain the configured
one-second TS offset to within the TS clock resolution. All 126 generated
playlist/segment files match between managed and native commands, including the
video-only reference outputs.

A complete 30-minute A/V input with repeated 96x80 video and continuous 48 kHz AAC
produces two variants with 301 segments each. Every one of 10,802 pictures and
84,376 audio packets survives in each variant; independent picture samples,
presentation intervals, audio bytes and full decoded PCM hashes match. This
checks complete A/V processing and bounded media storage on a small source,
without establishing HD throughput or independent audio restart quality.

Encoded-audio checks cover complete AAC, MPEG-1 Layer III and MPEG-2 Layer III
inputs across six variants, 160 pictures and 54 segments. Independently selected
source PCM is encoded separately; every resulting AAC byte and all independently
decoded PCM match the audio in the HLS playlists. Every audio PTS/DTS retains its
exact movie origin minus AAC priming and the 1024-sample cadence, rounded to the
transport clock. Managed and Native AOT commands produce 129 identical transport
and playlist files, including reference outputs. Tests also cover fractional
sample origins, absent edits, finite clipping, pending packet cancellation,
resource disposal and rejected audio options.

The full 30-minute A/V input also passes with newly encoded audio: 86,400,000
selected mono PCM samples produce 84,376 AAC packets per variant, including
1024 priming samples and no final padding. Both variants preserve all 10,802
video pictures across 301 segments each. Every audio packet and the complete
independently decoded PCM match separate encoding of the selected source PCM;
every audio PTS/DTS follows the exact expected timeline. This uses repeated
96x80 video and a continuous audio signal. It does not establish perceptual
quality or HD throughput. In-progress SIGINT returns 130 and removes the
unpublished directory, including all temporary segments.
