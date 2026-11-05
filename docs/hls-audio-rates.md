HLS encoding supports a separate raw AAC bit ceiling for each video variant:

```bash
mediana hls encode input.mp4 output-hls --audio-stream 1 --audio-codec Aac --audio-bit-rates 96000,128000,192000 --widths 1920 1280 854
```

Bitrates correspond to widths in the same order. Supply one positive value per
width, with `--audio-codec Aac`. `--audio-bit-rate` selects one ceiling for all
widths and cannot be combined with `--audio-bit-rates`. Omission retains the
128000 bits/second common ceiling. Invalid syntax, count, codec budget or conflicting
options fail without publishing output. These values bound raw AAC access units;
they exclude TS overhead and do not guarantee a target file size or perceptual quality.

## Shared input and independent encoding

The selected audio track is decoded once. Movie edits and the optional explicit
mono/stereo matrix are applied once. Each converted Float32 PCM frame is borrowed
by the distinct encoder pipelines; the operation does not accumulate the full
recording or decode again for another bitrate. Equal bitrates reuse the same AAC
encoder and packets, including nonadjacent variants in the requested order.

`Mediana.Streaming.Hls` exposes this preparation as `HlsAacPcmSource` and
`HlsAacAudioSource`. The caller supplies a `MediaEngine` with audio decoders and
the shared allocator; container factory registration is not required. The MP4
reader is opened once for edits and packet indexing, then transferred to
`MediaEngine.OpenAudio(reader, allocator, streamIndex)`. Decoder creation failure
leaves reader ownership with the caller. The source remains borrowed throughout.
`HlsCopyAudioSource` exposes the corresponding indexed audio-copy path.

The PCM source applies explicit movie edit windows and `AudioEncodingPcm` once.
It does not own unrelated allocations in the shared budget. Each returned frame
belongs to the caller; source operations must not overlap. The CLI uses these
same library types for its audio variants.

`MediaEncodeGroup<TFrame>` provides the format-independent library operation for
submitting a borrowed frame to multiple `MediaEncodePipeline<TFrame>` instances.
It copies registrations, rejects empty/duplicate/null targets, submits in order
and drains available packets through the existing pipeline. It introduces no
frame queue or worker thread. Its completion drains each encoder without
finalizing writers. Components remain borrowed; failure, cancellation or partial
submission makes the group terminal, and concurrent operations fail explicitly.

HLS supplies a pending output buffer for each distinct AAC encoder, bounded to
128 packets and the common media-buffer budget. Packets are delivered only to
their assigned variants before the next video decode timestamp. Video precedes
audio at equal DTS. EOF drains all encoder tails. Failure/disposal releases every
pending packet and codec; the output directory is published only after successful
completion and matching segment timelines.

All AAC variants share exact selected PCM timing, sample rate, channel order,
1024-sample encoder priming and final padding. Their bit ceilings affect encoded
bytes, rather than timing. Each VOD playlist and the master bandwidth fields are
measured from that variant's complete transport output. Audio-bearing playlists
retain the existing independent-segment policy.

## Complete short-source verification

Three sources cover AAC mono, AAC 5.1 with explicit stereo downmix, and MP3 in
MP4. Each creates two video widths at separate 96/192 kbit/s raw AAC ceilings.
The complete source edit is independently selected and encoded separately at each
ceiling. Every output audio packet matches that separate encoding byte-for-byte;
all PTS/DTS and raw packet bit budgets are checked. Every decoded video picture
matches the preceding video implementation, including separate segment decoding.

| Source | Selected PCM samples/channel | AAC packets/variant | Pictures/variant | Segments/variant | Final AAC padding |
| --- | --- | --- | --- | --- | --- |
| AAC mono | 76,800 | 76 | 8 | 3 | 0 |
| AAC 5.1 to stereo | 13,920 | 15 | 8 | 3 | 416 |
| MP3, 44.1 kHz | 317,520 | 312 | 36 | 12 | 944 |

All 45 transport/playlist files are byte-identical between managed and Linux x64
Native AOT runs. Native and independent complete AAC decoding agree within
`5.21540641784668e-8` over 1,495,040 Float32 scalar values; every value is finite.
Master peak/average bandwidth is independently recomputed from complete segment
sizes and durations. With a common 128 kbit/s ceiling, all three complete outputs
remain byte-identical to the preceding implementation.

Tests also compare the entire input range-read sequence for one encoder versus
three unique bitrates with a repeated setting: the reads are identical. They
check reuse of the repeated encoder, leading priming before video ties, final
sample accounting and cleanup after failure while creating a later encoder.
Cancellation with pending output makes the source terminal and disposal releases
all buffers. Invalid CLI rate arrays and conflicting options publish nothing.

## Complete 30-minute processing

A full 1800-second AAC mono track at 48 kHz and its 96x80 H.264 video were encoded
to three widths, 96/64/32, with respective raw AAC ceilings of 96/128/192 kbit/s.
The source contains 10,802 video pictures and selects 86,400,000 PCM samples per
channel. All three variants contain 301 aligned segments and 84,376 AAC packets;
the complete output contains 907 transport and playlist files. Final media-buffer
usage is zero.

Each complete audio sequence matches a separate AAC encoding of the independently
selected PCM at that ceiling, byte-for-byte. Every packet's PTS/DTS and raw bit
budget passes independent checks. Every decoded video picture matches the
preceding video implementation. Complete AAC decoding checks all 259,203,072
Float32 scalar values across the three variants; all are finite. Each master
playlist bandwidth field agrees with independently measured transport output.

| Width | Raw ceiling (kbit/s) | Actual raw rate (kbit/s) | Maximum independent PCM error |
| --- | --- | --- | --- |
| 96 | 96 | 93.666 | 8.940696716308594e-8 |
| 64 | 128 | 114.102 | 8.195638656616211e-8 |
| 32 | 192 | 118.885 | 8.940696716308594e-8 |

An isolated Linux x64 Native AOT run, restricted to one CPU core, measured
342.229 seconds of process CPU, 367.396 seconds elapsed and 27.13 MiB peak RSS.
This is a single full-source resource measurement on small, repeated video.
These repeated small-video results do not establish representative HD throughput
or quality. A separate [complete 720p/360p HLS comparison](hls-hd-encoding.md)
records both full-film audiovisual outputs against FFmpeg, including resources,
picture error, complete audio decoding and independent segment checks.

Cancellation was also exercised after real processing progress. The process
returned exit code 130, and neither the final output directory nor its temporary
directory remained.

The complete solution has 2531 passing managed tests. Release build, formatting,
license headers, strict Linux x64 Native AOT CLI and the complete native test
runner pass without analyzer or AOT suppressions.
