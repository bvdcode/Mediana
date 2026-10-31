# Complete HD decoding and stream-copy verification

The complete original 720p release of Big Buck Bunny was checked through the
public container, video-session, MPEG-TS and HLS operations. The source contains
1280x720 AVC Main 3.1 at 24 fps, 48 kHz AAC-LC with six channels, and a QuickTime
timecode track. Its declared duration is 596.461667 seconds.

Source: [Blender Foundation release](https://peach.blender.org/download/),
[original MOV mirror](https://www.peach.themazzone.com/big_buck_bunny_720p_h264.mov).
The film is © 2008 Blender Foundation / www.bigbuckbunny.org, released under
[CC BY 3.0](https://peach.blender.org/about/).
The 416,751,190-byte input has SHA256
`45c8bafeb9a53df7f491198d2e71529701bcf1cd51805782089fac1d32869f9b`.

## Complete results

- Native AOT video decoding returned all 14,315 pictures. Every visible YUV byte,
  PTS and frame duration matched independent FFmpeg frame hashes. The comparison
  covered 19,789,056,000 decoded bytes and reached EOF with no retained media storage.
- Video-only MPEG-TS copying preserved every decoded picture and duration. Every
  output PTS was exactly one second after its source PTS.
- Default A/V copying produced 42,275 packets. The Native AOT and managed TS files
  were identical. Their independently extracted ADTS audio matched all 27,960
  original AAC packets, including framing and channel declarations.
- HLS copying produced 100 TS segments and a finalized VOD playlist. Every video
  picture, PTS and duration matched independently; every AAC packet matched the
  original stream. All 101 Native AOT and managed files were identical.
- The playlist duration was 596.48 seconds, including the final coded audio tail.
  The six-channel audio result establishes stream preservation during copying.

QuickTime signed composition offsets use their declared format semantics.
Linear-movie timestamps advance the decode schedule by the complete table's
required lead, preserve presentation times, and merge tracks by adjusted DTS.
Default CLI copying selects audio and video; timecode remains available to probing
and explicit stream selection. See [module contracts](architecture.md).

## CPU and memory

Linux x64, Intel Core i5-12450H, .NET 10.0.13 Native AOT and FFmpeg
6.1.1-3ubuntu5. Each process ran sequentially with affinity to the same logical
CPU; FFmpeg decoding used one thread. One full-source run was recorded per
implementation on a shared host. CPU time is user plus system time; memory is
process peak RSS.

| Complete operation | Mediana CPU | FFmpeg CPU | Mediana peak RSS | FFmpeg peak RSS |
| --- | ---: | ---: | ---: | ---: |
| Decode video and hash every picture | 654.54 s | 84.68 s | 106.1 MiB | 74.7 MiB |
| Copy video and audio to MPEG-TS | 0.863 s | 0.857 s | 30.2 MiB | 57.5 MiB |
| Copy video and audio to VOD HLS | 2.352 s | 0.903 s | 28.8 MiB | 78.0 MiB |

The decode measurement includes SHA256 and per-picture text output; the Native
consumer also checked reference hashes and exact times while decoding. It measures
that complete operation, rather than an isolated codec kernel. FFmpeg and Mediana
use their respective transport muxing policies. Native decoding was measured at
revision `70ff4fb`; corrected movie-timeline copying at `5d6e272`.

Representative HD re-encoding quality and throughput remain open. The decode
operation's observed CPU ratio, 7.73, is the current performance baseline.
