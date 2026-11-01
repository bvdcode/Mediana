The complete original Big Buck Bunny MOV was transcoded into an audiovisual
HLS ladder with 1280x720 and 640x360 variants. Both the Mediana and FFmpeg
operations reached the original film's EOF. The measurements below cover the
whole operation, including source decoding, resizing, stereo mixing, two video
encoders, two AAC encoders and all transport/playlist output.

## Source and settings

The [original HD source](real-hd.md) contains 14,315 pictures at 24 fps,
596.458333 seconds of video and six-channel AAC at 48 kHz. Its 416,751,190 bytes
have SHA256 `45c8bafeb9a53df7f491198d2e71529701bcf1cd51805782089fac1d32869f9b`.
Movie edits select exactly 28,630,160 audio samples per channel, starting at zero.
Both operations apply the [normalized ITU stereo matrix](audio-downmix.md),
omitting LFE, before encoding the selected audio.

Mediana uses the Native AOT build at `67fc7b7`. The comparison uses FFmpeg
6.1.1-3ubuntu5 with libx264 on Linux x64, an Intel Core i5-12450H and .NET
10.0.13. The two measured processes ran sequentially, restricted to logical
CPU 2. Mediana used `DOTNET_PROCESSOR_COUNT=1`; FFmpeg used one input decoder
thread, one filter-complex thread and one thread per output codec. Independent
decoding and verification started after both measured processes terminated.
These are single complete-process observations on a shared host.

Both video outputs use progressive YUV420P8, Constrained Baseline level 4.1,
BT.709 labels, no B pictures and an IDR every 144 pictures. The first 99 segments
contain 144 pictures each; the final segment contains 59. Mediana uses initial
QP26, per-variant packet rates of 10/5 Mbit/s and respective capacities of
20/10 Mbit. FFmpeg uses `veryfast`, targets and maximum rates of 10/5 Mbit/s,
VBV sizes of 20/10 Mbit and disabled scene-cut keyframes. Its VBV and Mediana's
packet envelope have different semantics and encoder searches.

The 720p variant uses stereo AAC at 96 kbit/s; 360p uses 128 kbit/s. The distinct
settings verify separate AAC states fed by the same decoded and mixed input.
Mediana bounds raw AAC access units; FFmpeg's settings are bitrate targets.
Both use 48 kHz, MPEG-TS segments and six-second VOD playlists.

## Complete resources and video error

CPU is user plus system time; RSS is process peak resident set size. Published
bytes include all 203 files of each complete ladder.

| Implementation | CPU | Elapsed | Peak RSS | Published bytes | Processing speed |
| --- | ---: | ---: | ---: | ---: | ---: |
| Mediana Native AOT | 1683.365 s | 1798.004 s | 139.34 MiB | 1,172,197,543 | 0.332x |
| FFmpeg / libx264 veryfast | 186.728 s | 192.325 s | 189.48 MiB | 1,136,231,473 | 3.101x |

Mediana used 9.02 times the CPU, 26.46% less peak RSS and 3.17% more output
bytes on this operation. Processing speed divides the original video duration
by elapsed time and includes both variants and audio. This does not establish
equal-quality throughput or general performance across other inputs.

PSNR uses every visible decoded Y, U and V sample, weighted by sample count.
The 720p reference is the independently decoded original; 360p uses an
independent area resize of that original.
AVC packet rates count each VCL NAL plus its four-byte packet prefix, excluding
parameter sets, access-unit delimiters and transport overhead.

| Implementation | Dimensions | Actual AVC packet rate | Y PSNR | U PSNR | V PSNR | Aggregate PSNR |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Mediana | 1280x720 | 10017.148 kbit/s | 44.360 dB | 48.654 dB | 49.324 dB | 45.429 dB |
| FFmpeg / libx264 | 1280x720 | 9825.902 kbit/s | 48.224 dB | 51.764 dB | 52.630 dB | 49.188 dB |
| Mediana | 640x360 | 4921.828 kbit/s | 45.866 dB | 46.849 dB | 48.076 dB | 46.325 dB |
| FFmpeg / libx264 | 640x360 | 4801.256 kbit/s | 47.305 dB | 50.586 dB | 51.194 dB | 48.204 dB |

The observed video error is higher in both Mediana variants. These numeric
errors do not establish perceptual or listening acceptance.

## Complete output verification

All four continuous video streams were independently decoded and then decoded
through the current Native AOT Mediana consumer. Every picture, visible byte,
PTS and duration matched: 57,260 pictures and 49,472,640,000 visible YUV bytes
in total. Each of the 400 segments was also decoded by a fresh independent
decoder; its pictures and global timestamps matched the continuous stream.
All coded VCL bytes remained unchanged through the verification MP4 remux.
Every operation reached EOF and released media storage.

All transport video PTS/DTS values have the exact 1/24-second cadence. All
canonical MP4 packet durations equal 1/24 second. FFprobe omits the duration
field for the first 21 packets of each Mediana transport stream; the remaining
duration fields agree with that cadence. An unknown packet duration is allowed
by the [AVPacket contract](https://ffmpeg.org/doxygen/6.1/packet_8h_source.html).

Both Mediana playlists contain 100 aligned segments, ending at 596.48 seconds
including the audio tail. Every AAC stream contains 27,961 access units,
1024 priming samples and 880 padding samples. Audio starts exactly 1024/48000
seconds before video; every audio PTS, DTS and duration was checked. Each
Mediana AAC stream matches a separate complete encoding of the independently
selected PCM byte for byte, and every access unit satisfies its fractional
bit budget. Independent decoding of the two Mediana AAC streams matches all
114,528,256 Float32 values within `2.384185791015625e-7`.

FFmpeg's AAC output uses perceptual noise substitution (PNS). PNS reconstructs
signalled noise energy with random vectors, so independent decoders need not
produce identical noise samples; see ISO/IEC 14496-3:2001, Subpart 4, section
4.6.13, and the [reference PNS implementation](https://github.com/knik0/faad2/blob/master/libfaad/pns.c).
Both complete FFmpeg audio streams were decoded by Mediana, FFmpeg and FAAD2
2.11.1. All samples are finite and counts agree. All 22,396 access units free
of current/previous PNS agree across all three decoders within
`2.086162567138672e-7`. Differences beyond the unchanged `2e-6` tolerance occur
only where PNS can affect the samples. Whole-channel energy differs by at most
0.00588% across the decoders. No pointwise identity is claimed for PNS samples.
The existing source AAC decoder differences described in the
[HD audio verification](real-hd.md) also prevent ranking AAC encoders from
this full-pipeline comparison alone.

Every one of the 6,235,064 Mediana transport packets passed continuity-counter
checks across segment boundaries. All PAT/PMT CRCs passed; all 57,262 PCR samples
are monotonic, with at most 40 ms between them in each variant. Master playlist
peak/average bandwidth fields match independently measured segment sizes and
durations, and actual SPS/AAC configurations match the advertised CODECS.
Every consecutive AVC packet window satisfies the configured Mediana envelope.
This packet-envelope check does not establish Annex C HRD conformance.

## Mediana command

The original MOV has video stream 0 and audio stream 2:

```sh
DOTNET_PROCESSOR_COUNT=1 taskset -c 2 mediana hls encode input.mov output-hls --audio-stream 2 --audio-codec Aac --audio-channels 2 --audio-bit-rates 96000,128000 --segment-ms 6000 --qp 26 --video-bit-rates 10000000,5000000 --video-buffer-ms 2000 --no-color --widths 1280 640
```
