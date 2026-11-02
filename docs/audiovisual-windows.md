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

Audio is decoded from the beginning, applies the supported MP4 edit list, then
passes through `AudioTrimmer.CreateTimeWindow`. Both boundaries select the first
sample instant at or after the requested time, with exact rational arithmetic.
The original sample grid and timestamps are preserved. A short track ends at its
actual EOF, and leading empty edits do not generate silent samples. Complete PCM
blocks retain their storage; partial boundary blocks copy only selected samples.

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

Eight Linux x64 Native AOT outputs were decoded independently and inspected in
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

Selected picture and PCM counts for the three adjacent intervals sum to the
whole interval. All video PTS and AAC sample-grid PTS match independently derived
expectations. Every stream has increasing DTS, and each output starts with an
independently decodable video picture. Each encoder reports its priming and
padding, and every sample run ends with zero retained media buffer bytes.

FFmpeg and Mediana decoding of each encoded output produce identical YUV bytes.
Video checking uses an MP4 remux with normalized parameter-set framing; every
original and remuxed coded slice is verified byte for byte. Their decoded AAC
Float32 output differs by at most 5.22 × 10⁻⁸. These comparisons check decoding
agreement on the encoded output, rather than lossless reproduction of the input.

The complete validation also passes 3,011 managed tests, 580 audio tests with
hardware intrinsics disabled, and the package-only Native AOT runner for all 30
packages. Strict Native AOT compilation succeeds for the sample and CLI. Separate
time-window tests enumerate sample instants using arbitrary-precision rational
arithmetic and check real edited AAC against independent PCM references.

## Remaining work

The sample decodes the AAC prefix to preserve overlap and other decoder state.
Efficient indexed audio restart needs a codec-aware preroll contract and its own
independent correctness checks.

Separately encoded AAC intervals each carry their own priming and padding.
Concatenating the resulting transport files is not proven to produce gapless
audio. A streaming composition must explicitly account for those samples and
validate the joins; exact input selection alone does not establish that result.
Apple describes the priming/remainder distinction in
[AAC gapless playback guidance](https://developer.apple.com/library/archive/qa/qa1636/_index.html).

This interval check does not measure throughput or resolve the outstanding
H.264 encoding CPU and compression-efficiency gaps.
