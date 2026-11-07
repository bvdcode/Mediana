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

`IContainerPresentationReader` exposes explicit presentation selections without
media reads. MP4 interprets one unit-rate media edit, optionally preceded by an
empty delay edit, on the reader's selected packet timeline. These declarations
intersect requested encoding intervals. Absence of an explicit selection remains
distinct from an empty selection. Nonlinear edits are rejected.
The [QuickTime edit-list specification](https://developer.apple.com/documentation/quicktime-file-format/edit_list_atom/edit_list_table)
defines separate movie-duration and media-start units. The operation does not
infer additional undocumented codec delay from container names.

Compressed interval copy requires exact packet boundaries and a contiguous range
in decode order. Video boundaries additionally require codec-inspected independent
access packets at the beginning and the next boundary, unless the interval reaches
EOF. Misaligned copy requests fail without enlarging the range or re-encoding it.
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
Dispose the session after success, cancellation or failure. Other track resources
are released even when individual disposal fails. Failed container output must be
discarded. `ProcessAsync` also owns its opened container and writer, while borrowing
the supplied byte source and destination stream.

Sequential-only demuxers are supported by the existing decoding APIs. Independent
multi-track processing currently requires indexed packets. Independent audiovisual
HLS encoding, including continuous audio packet grids across adjacent requests,
is not yet supplied by this operation.

## Verification

Managed tests exercise whole audiovisual encoding, video copy with audio encoding,
noncontiguous track identities, fractional interval bounds, exact PCM counts,
encoder delay/padding, timestamp-ordered muxing, shared read limits and source
ownership. Repeated cold requests for the final second of a 12-second reordered
AVC/AAC source encode five pictures and 48,000 PCM samples while reading only the
needed source region. Cancellation, output errors and disposal release all track
buffers. Linux Native AOT runs the scaled audiovisual interval and compares repeated
complete MPEG-TS outputs. Independent FFmpeg decoding accepts both output tracks.
