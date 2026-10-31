The HLS source module can segment encoded AVC video as packets arrive and format
VOD media/master playlists, including optional AAC-LC or MPEG-1/2 Layer III
audio. The CLI can copy the original audio or encode it with the AAC source module.
Use `--audio-channels 1` or `2` with `--audio-codec Aac` for an explicit
normalized [mono/stereo downmix](audio-downmix.md), including multichannel sources.
Source movie edits precede mixing and new encoder priming/padding remain in TS.
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

The AAC encoder uses the selected sample rate/channel order. With one shared audio
bit ceiling, all variants receive exactly the same encoded audio packets.
`--audio-bit-rate` requires `--audio-codec Aac`; its default is 128000 raw bits/second.
Alternatively, `--audio-bit-rates` supplies one positive raw AAC ceiling per width,
in the same order, and cannot be combined with `--audio-bit-rate`. Identical
settings share one encoder; each distinct bit ceiling has an independent encoder.
All consume the same decoded, edited and converted PCM frames. For example:

```bash
mediana hls encode input.mp4 output-audio-rates --audio-stream 1 --audio-codec Aac --audio-bit-rates 96000,128000,192000 --widths 1920 1280 854
```

It is a variable bitrate ceiling excluding transport overhead, rather than a target
file size. The source's explicit edit removes its declared codec delay; newly
encoded AAC introduces its own 1024-sample priming. That packet starts before the
selected PCM origin. Final drain emits the overlap tail and at most 1023 padding
samples; TS retains them without gapless trimming metadata. No priming is repeated
at segment boundaries. The playlist's final duration includes the encoded tail.

Audio decoding, edit selection and PCM conversion happen once for all widths.
The generic `MediaEncodeGroup<AudioFrame>` submits each borrowed frame to the
distinct encoder pipelines sequentially and drains their available output.
Each encoder has a bounded pending packet buffer; delivery writes only packets
before the next video DTS to its assigned variants. Video precedes audio at equal DTS. PCM conversion,
trimming and encoder working storage use bounded frame buffers; the recording is
not collected in memory. Decoders still read their own sequential container data.
The command reports input PCM count and final AAC priming/padding at completion.
[Complete per-variant checks](hls-audio-rates.md) include unchanged uniform-rate
output, independent AAC bytes, full PCM and measured master playlist bandwidth.
[AAC quality and rate limits](aac-encoding.md) apply to this path too.

By default the mode uses one fixed QP for all variants, with IDR groups and P16x16/skip
inter-frame compression. Each encoder uses the requested segment duration as
its media-time IDR interval; the first eligible frame starts the next aligned
segment. One reconstructed reference is retained per variant.
Optional `--video-bit-rates 1500000,400000 --widths 640 320` selects independent
adaptive packet-rate controllers, one rate per width in the same order.
`--video-buffer-ms` sets their common buffer duration, defaulting to 2000.
The limit counts AVC packet bytes and excludes inserted parameter sets, audio
and transport overhead; master bandwidth still measures complete TS segments.
There is no filler or complete HRD timing/conformance claim.
See [H.264 encoding](h264-encoding.md#constrained-variable-packet-rate).
Per-variant audio rates, arbitrary display transforms, live/event playlists,
fragmented MP4, encryption
and playback scheduling remain outside this path.

## Checks

The explicit all-IDR corpus compares all visible samples in 48 output pictures
against independent decoder references, including resized and non-square-pixel
video. Their 15 segments preserve every AVC packet, presentation time, TS table,
clock and continuity counter. The same verifier is included in the Native AOT
runner. Tests also cover non-seekable outputs, ownership, cancellation, I/O failure,
overlapping calls, false key-frame flags, repeated PTS, segment limits, undefined
short-video peak rates, injected metadata and atomic CLI directory output.

Current I/P HLS commands independently verify six variants, 160 video pictures,
1090 AAC packets and 54 segments per mode, including every individual segment.
All video pictures match separate video-only encoding, and full audio bytes/PCM
and PTS/DTS match separate audio encoding. All 129 managed/Native AOT TS and
playlist files are byte-identical.

The [complete temporal-encoding measurement](h264-encoding.md#complete-beforeafter-measurement)
checks all 10,802 pictures of a 30-minute video-only conversion. Long audiovisual
HLS with the current I/P encoder remains to be checked.

### Earlier all-IDR command checks

The following complete command checks used the original all-IDR encoder.

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

## Complete original HD source

A complete 596.46-second 1280x720 MOV with six-channel AAC has been verified
through Native AOT decoding, explicit stereo downmix and two rate-controlled
320x180/160x90 HLS variants. All 14,315 pictures, both complete audio tracks,
packet clocks, aligned segments and video rate windows were independently
checked. [Full counts, resource measurements and output scope](audio-downmix.md#complete-audiovisual-hls-encoding)
are recorded with the shared mixing workflow.

The same complete source also passes two 1280x720/640x360 variants with QP 26,
two-second video boundaries, respective AVC ceilings of 10/5 Mbit/s with
two-second packet buffers, and separate stereo AAC ceilings of 96/128 kbit/s.
Each variant contains all 14,315 pictures, 27,961 AAC packets and 299 aligned
segments. The complete package contains 601 transport and playlist files.

Every visible decoded YUV byte, picture timestamp and duration matches between
Native AOT and an independent decoder: 24,736,320,000 visible bytes across the
two variants. Video in all 598 segments also decodes independently with its original
global video timestamps and exactly the same pictures. The 1280x720 pictures
are identical to the [complete HD encoding verification](h264-hd-encoding.md).

Both complete audio payloads match separate encodings of the independently
selected 28,630,160 PCM samples per channel. All 114,528,256 decoded Float32
values are finite and agree with the independent decoder within `2e-6`.
Every audio packet clock and raw bit ceiling passes. Encoder priming is 1024
samples and final padding is 880 samples per channel.

All 6,243,115 transport packets pass continuity checks across segment boundaries;
every PAT/PMT section CRC passes, PCR remains monotonic and its maximum gap is
40 ms. Actual SPS bytes match the master codec declarations. Peak and average
master bandwidth values match independent calculations over every segment.

A Native AOT process restricted to one CPU core measured 1812.546 seconds
of process CPU, 1908.223 seconds elapsed and 127.16 MiB peak RSS on Linux x64.
This full-source measurement uses the allocating identity-resize path. It does
not establish matched FFmpeg throughput or perceptual encoding quality.
