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
its allocator-owned media storage. The library source and fixtures match the
validated 3,181-test, 30-package build described in
[audio group verification](hls-audio-groups.md#verification-and-remaining-work).

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
Browser-player behavior and exact VOD presentation-range handling around priming
and padding remain unverified. The existing audiovisual interval sample still
initializes AAC separately for each request; it has not been converted to this
prepared-audio composition.

An additional transport check reads original PES timestamps and every complete
ADTS frame directly. In a separate FFmpeg HLS output, 564 AAC access units across
74 PES packets form an exact continuous sample grid, while `ffprobe` assigns
incorrect timestamps to six initial access units. Direct transport inspection
resolves that discrepancy; packet timestamp interpolation alone is insufficient
evidence of a media discontinuity.

The preparation and request checks do not measure throughput or resolve the
remaining H.264 CPU, quality and compression-efficiency work.
