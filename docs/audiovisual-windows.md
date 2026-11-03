# Audio/video presentation intervals

The public processing and execution APIs can select a movie-time interval,
encode H.264 video and AAC audio, and merge the resulting packets into MPEG-TS.
The executable sample composes these operations explicitly:

```sh
dotnet run --project samples/Mediana.Sample -c Release -- --transcode-av-window input.mp4 output.ts 213 987
```

The last two arguments are the inclusive start and exclusive end in milliseconds.
The destination must not exist. This sample requires supported MP4 AVC/AAC tracks
and an identity video display transform. It explicitly mixes audio to stereo
Float32 before AAC encoding. It uses no external media executable at runtime.

## Selection and timing

Video uses the MP4 movie timeline, indexed seek and `VideoTrimmer`. Whole pictures
whose presentation times fall in the requested interval are selected; their
durations remain unchanged, so the last picture may extend beyond the end.

Audio uses indexed `AudioSession.SeekAsync` on the MP4 movie timeline. The
decoder reconstructs the required AAC history before returning PCM. Two composed
`AudioTrimmer.CreateTimeWindow` operations intersect the explicit edit and requested
intervals. `Mp4AudioSampleRange.GetPresentationEnd(decodedSampleRate)` supplies the
first excluded sample instant; media-zero sample indices are never reapplied as
relative skips after seeking. Both boundaries select the first sample instant at or after the requested time, with exact rational arithmetic.
The original sample grid and timestamps are preserved. A short track ends at its
actual EOF, and leading empty edits do not generate silent samples. Complete PCM
blocks retain their storage; partial boundary blocks copy only selected samples.
Decoded audio timestamps must be continuous; discontinuities fail explicitly.

`MediaEncodeSource<VideoFrame>` and `MediaEncodeSource<AudioFrame>` drain their
encoders independently. `MediaPacketMerge.WriteAsync` holds one pending packet
per source and writes the earliest DTS across their respective clocks. It
validates stream identity and per-stream DTS order, releases pending packets on
failure, and finalizes the borrowed writer only after every source reaches EOF.
The sources borrow their encoders; frame callbacks transfer ownership.

Original movie timestamps are retained. The MPEG-TS writer applies its default
one-second common offset once. AAC's first packet starts 1,024 samples before
the first selected PCM sample to represent encoder priming; final frame padding
is reported separately. Encoded packet timestamps therefore differ from the
interval of audible input samples.

## Independent checks

Ten Linux x64 Native AOT outputs were decoded independently and inspected in
full, including every video picture, audio sample and packet timestamp:

| Input and interval | Selected pictures | Selected PCM samples per channel |
| --- | ---: | ---: |
| AVC/AAC, 0–213 ms | 2 | 10,224 |
| AVC/AAC, 213–987 ms | 3 | 37,152 |
| AVC/AAC, 987–1,600 ms | 3 | 29,424 |
| AVC/AAC, 0–1,600 ms | 8 | 76,800 |
| Delayed audio, 0–213 ms | 2 | 1,680 |
| Delayed audio, 213–987 ms | 3 | 37,152 |
| Variable-rate video, 213–987 ms | 21 | 37,152 |
| Short 5.1 input, 213–987 ms | 3 | 3,696 |
| Edited 44.1 kHz stereo, 213–987 ms | 3 | 6,923 |
| 44.1 kHz stereo without edits, 213–987 ms | 3 | 8,014 |

Selected picture and PCM counts for the three adjacent intervals sum to the
whole interval. All video PTS and AAC sample-grid PTS match independently derived
expectations. Every stream has increasing DTS, and each output starts with an
independently decodable video picture. Each encoder reports its priming and
padding, and every sample run ends with zero retained media buffer bytes.

FFmpeg and Mediana decoding of each encoded output produce identical YUV bytes.
Video checking uses an MP4 remux with normalized parameter-set framing; every
original and remuxed coded slice is verified byte for byte. Their decoded AAC
Float32 output differs by at most 7.46 × 10⁻⁸. These comparisons check decoding
agreement on the encoded output, rather than lossless reproduction of the input.

The complete validation also passes 3,067 managed tests, 636 audio tests with
hardware intrinsics disabled, and the package-only Native AOT runner for all 30
packages. Strict Native AOT compilation succeeds for the sample and CLI. Separate
time-window tests enumerate sample instants using arbitrary-precision rational
arithmetic and check real edited AAC against independent PCM references. Indexed
and sequential selection both cover missing edits, leading delays, fractional
boundaries, empty edits and ranges that extend beyond actual decoder EOF.

## Independently encoded interval boundaries

A further Linux x64 Native AOT check uses a real 1280×720, 24 fps movie with
48 kHz, 5.1 AAC input. It encodes 0–6 seconds and 6–12 seconds independently,
then encodes 0–12 seconds as one interval. Every output is decoded completely
by FFmpeg, and every output packet timestamp is inspected.

| Interval | Pictures | Selected PCM samples per channel | Encoded AAC packets | Priming samples | Padding samples |
| --- | ---: | ---: | ---: | ---: | ---: |
| 0–6 s | 144 | 288,000 | 283 | 1,024 | 768 |
| 6–12 s | 144 | 288,000 | 283 | 1,024 | 768 |
| 0–12 s | 288 | 576,000 | 564 | 1,024 | 512 |

The independently encoded files decode to 579,584 samples per channel in total;
the continuous interval decodes to 577,536. Raw decoding retains each encoder's
priming and padding. The independent files therefore contain 2,048 more samples
per channel than the continuous file, despite selecting the same 576,000 input
samples. The sample reports zero retained media buffer bytes after each run.

Every AAC packet within each file advances by 1,920 ticks at 90 kHz. Across the
independent boundary, however, the next file's first AAC DTS precedes the
previous file's last DTS by 1,440 ticks. The coded audio extents overlap by
3,360 ticks, or 37.333 milliseconds. These files cannot be treated as consecutive
parts of one continuous AAC packet sequence merely by concatenating them.

A separate control runs FFmpeg with the server-style independent seek, duration,
timestamp offset, H.264 veryfast/CRF 25, and stereo 128 kbit/s AAC options. It
also produces 283, 283 and 564 AAC packets for the same intervals. Its timestamp
handling differs, and its independently encoded boundary also overlaps. This
control does not establish seamless playback for either implementation, nor does
it compare perceptual AAC quality or encoding throughput.

For continuous HLS audio, the existing full-file HLS path retains one AAC encoder
per audio bitrate across segment boundaries and partitions complete coded access
units. Independent video requests need an audio preparation strategy that
preserves that continuous encoded timeline. Repeated AAC encoder initialization
is not equivalent to partitioning a continuous stream.

The transport writer also supports [audio-only segment continuation and reusable
snapshots](audio-transport-segments.md). AAC and MPEG-1/2 Layer III packet windows
preserve continuous clocks and counters, including out-of-order regeneration.
This supplies a transport building block for prepared audio renditions; the
independent audiovisual sample still creates a fresh AAC encoder per request.

[`AudioHlsWriter`](audio-hls.md) also prepares a separate continuous audio media
playlist. A 12-second, six-second-target AAC preparation uses one encoder and
preserves every coded packet and decoded sample across its files. This establishes
the audio preparation step; the independent audiovisual sample has not yet been
integrated with that separate rendition or master-playlist audio groups.

[RFC 8216, section 6.2.1](https://www.rfc-editor.org/rfc/rfc8216.html#section-6.2.1)
describes division of continuous encoded media at packet and key-frame boundaries.
The [discontinuity tag](https://www.rfc-editor.org/rfc/rfc8216.html#section-4.3.2.3)
declares changes in timestamp sequence or encoding sequence; it does not specify
sample-level AAC priming or padding removal. Apple describes those distinct
sample counts in its
[AAC gapless playback guidance](https://developer.apple.com/library/archive/qa/qa1636/_index.html).

## Remaining work

Gapless joins of independently requested encoded intervals require a continuous
audio strategy and playback validation. Exact input selection and independently
decodable transport files alone do not establish that result.

This interval check does not measure throughput or resolve the outstanding
H.264 encoding CPU and compression-efficiency gaps.
