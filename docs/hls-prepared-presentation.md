# Prepared audio and independently requested HLS video

A Linux x64 Native AOT consumer combines one prepared continuous audio rendition
with independently regenerated video files through `HlsMasterPlaylist`. The check
uses 12 seconds of a real 1280×720, 24 fps movie with 48 kHz 5.1 AAC audio.
Every selected picture and decoded sample is checked through the actual master
playlist, including the regenerated video files.

## Composition

Audio selection uses the MP4 movie timeline, explicit sample edits and a
12-second `AudioTrimmer` interval. Stereo Float32 downmix feeds one AAC encoder at
128 kbit/s for the entire interval. `AudioHlsWriter` produces a separate audio
media playlist; `CreateRendition` supplies its codec, channel count and measured
transport rates.

Video uses indexed `VideoSession.SeekAsync`, `VideoTrimmer`, 320×180 area resizing
and an independent H.264 encoder for each six-second request. A preparation pass
captures `MpegTsSegmentState` before the second interval. The original writer,
encoder and session are then disposed; the original allocator has zero live media
bytes. Requests in order 1, 0, 1 reuse that transport state through
`MpegTsWriter.ResumeSegment`, or create the
initial segment through `MpegTsWriter.Create`.

The snapshot contains transport configuration, clocks and counters; it retains no
encoded packets, decoded pictures or audio history. This check establishes reuse
of a prepared boundary. It does not establish generating arbitrary transport
continuations without preparation of preceding state.

`AvcHlsWriter` and `AudioHlsWriter` also accept a destination factory with
`(number, state, cancellationToken)` arguments. The first segment receives a null
state. Subsequent calls receive the immutable `MpegTsSegmentState` immediately
before the writer begins that segment. Applications can retain these snapshots
without running an additional transport writer during preparation:

```csharp
List<MpegTsSegmentState?> states = [];
await using AvcHlsWriter writer = AvcHlsWriter.Create(video,
    (number, state, token) =>
    {
        states.Add(state);
        return OpenSegmentAsync(number, token);
    }, allocator);
```

After preparation and disposal, use `MpegTsWriter.ResumeSegment` with a retained
non-null state, a new destination and an allocator. The initial segment uses
`MpegTsWriter.Create` with the original stream configuration and transport options.
The destination remains borrowed by these transport writers. A factory failure
faults the HLS writer; a previously delivered snapshot remains reusable. Publish
only media whose writing and completion succeeded. This preserves the continuous
timestamps and counters required by
[RFC 8216, section 3](https://www.rfc-editor.org/rfc/rfc8216.html#section-3).

These snapshots do not restore encoder or audio decoder history. Recreating
audio-containing segments requires the original continuous audio packets.
Independent video encoding still requires an IDR and unchanged codec configuration
and presentation timestamps. The original two-argument factories do not capture
transport snapshots.

The master associates a video-only playlist with the prepared audio group:

```csharp
HlsVariant associated = video with { AudioGroupId = audio.GroupId };
string master = HlsMasterPlaylist.Create([associated], [audio]);
```

Both media playlists advertise `TARGETDURATION:6`. All packet timestamps retain
their source movie origin plus the transport writer's common one-second offset.
The master records measured peak/average rates of 867,856/764,081 bits per second,
AVC and AAC codec identifiers, stereo audio and 320×180 video dimensions.

## Complete independent decoding

FFmpeg independently opens the generated master playlist, selects its audio and
video, and decodes both streams completely. Packet inspection checks every PTS
and DTS. The video files also pass continuity-counter and PCR checks across their
boundary. Both files begin with program tables and an IDR picture.

| Result | Verified extent |
| --- | --- |
| Selected input audio | 576,000 samples per channel |
| Encoded audio | 564 AAC access units; 577,536 samples per channel |
| Encoder priming / padding | 1,024 / 512 samples per channel |
| Audio segment packet counts | 282 / 281 / 1 |
| Selected video | 288 pictures; 144 per request |
| Video request order | Second interval, first interval, second interval |
| Repeated second request | Identical TS bytes |
| Master and video-media playlist decoding | Every YUV420P8 byte matches the continuous video reference |
| Individual video-file decoding | All 144 pictures match the corresponding reference interval |
| Master audio decoding | Every Float32 PCM byte matches the uninterrupted audio reference |

An independently initialized encoder can produce different coded bytes from
a continuous encoder while reconstructing the same pictures. The second video
interval has this behavior: its TS bytes differ from the continuous reference,
its transport byte count is equal, and all reconstructed pixels match. Repeating
the same independent request with the same snapshot produces identical bytes.
The video peak/average rates are checked against the actual requested file sizes.

The public package consumer builds with strict Native AOT warnings and releases
its allocator-owned media storage. Preparation captures the boundary directly
from the HLS destination factory using a single transport writer. Every output
file matches the earlier presentation, including the independently requested
video files.

The corresponding Release build passes 3,198 managed tests, verification of all
30 packages, the complete package-only Native AOT test runner and Native AOT CLI
build. Shared managed and Native tests replay AVC, AAC and MPEG-1/2 Layer
III segments from captured states after disposing their original HLS writers.
Additional tests check reusable state and destination ownership after factory
failure and cancellation.

## Coded clocks and presentation boundaries

AAC's first coded packet precedes the first input PCM sample and video picture
by 1,024 samples. Its last coded frame includes 512 padding samples. Continuous
coded packet coverage therefore differs from the selected audible interval:

| Track | First coded PTS, before transport offset | Coded end |
| --- | ---: | ---: |
| Video | 0 s | 12 s |
| Audio | −0.021333… s | 12.010666… s |

These clock differences do not indicate duplicated input selection. One encoder
is retained across audio boundaries, and every coded access unit occurs once.
The browser check below observes the remaining padding in the presentation's
duration; exact VOD presentation-range handling remains open. The existing
audiovisual interval sample still initializes AAC separately for each request;
it has not been converted to this
prepared-audio composition.

An additional transport check reads original PES timestamps and every complete
ADTS frame directly. In a separate FFmpeg HLS output, 564 AAC access units across
74 PES packets form an exact continuous sample grid, while `ffprobe` assigns
incorrect timestamps to six initial access units. Direct transport inspection
resolves that discrepancy; packet timestamp interpolation alone is insufficient
evidence of a media discontinuity.

## Browser playback and remaining presentation work

Chrome 156 on Linux with hls.js 1.7.4 opens the actual master over HTTP, selects
its stereo audio rendition and completes playback. Seeking to 5.8 seconds crosses
the six-second video boundary; seeking back to one second also reaches the end.
These are buffered seeks. All three paths finish without hls.js errors or an
`HTMLMediaElement` error. Video dimensions are 320×180. The video element is muted;
these checks do not establish listening quality or sample-exact browser PCM.

The browser reports an end time and duration of 12.010666 seconds. Its final audio
buffer ends there, while the video buffer ends at 11.999999 seconds. The observed
excess agrees, to the browser's microsecond reporting precision, with 512 padding
samples at 48 kHz. Basic playback therefore succeeds while the selected 12-second
presentation boundary remains unresolved.

This behavior is consistent with the
[MSE end-of-stream algorithm](https://www.w3.org/TR/2016/REC-media-source-20161117/#end-of-stream-algorithm),
which updates duration from the latest buffered track end, and with
[hls.js audio remuxing](https://github.com/video-dev/hls.js/blob/v1.7.4/src/remux/mp4-remuxer.ts),
which represents complete AAC frames. The
[Apple HLS authoring specification, section 8.7](https://developer.apple.com/streaming/hls-authoring-specification-for-apple-devices.html)
requires a common VOD media range; these playback results do not establish that
requirement for the current prepared presentation.

The browser also reports substantial dropped video frames:

| Playback conditions | Reported total frames | Reported dropped frames |
| --- | ---: | ---: |
| Mediana, background full pass | 292 | 143 |
| Mediana, visible replay, counter increments | 288 | 153 |
| x264 control, visible full pass | 292 | 134 |

The control encodes the same 288 reference pictures at 320×180 and 24 fps,
preserves every video PTS/DTS and uses the identical prepared AAC files. It also
reaches the end; hls.js reports one nonfatal `bufferAppendNoProgress` event for the
control. Corrupted-frame counters remain zero for all three passes. Initial-load
and seek work contributes to the cumulative frame counters.

The drops occur with both video encoders in this browser environment. Their cause
has not been isolated; smooth rendering and comparative playback performance
remain unverified. Other browsers, native HLS players and application integration
still require acceptance checks.

The preparation and request checks do not measure throughput or resolve the
remaining H.264 CPU, quality and compression-efficiency work.
