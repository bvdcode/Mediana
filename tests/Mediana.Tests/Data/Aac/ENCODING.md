The existing original WAV fixtures are encoded by Mediana's AAC-LC encoder and
decoded independently by FFmpeg 6.1.1. `ENCODING.json` records complete commands
and SHA-256 hashes. Float references use little-endian interleaved Float32 and
retain every 1024-sample frame, including analysis priming and final zero padding.

| Source | Raw bit ceiling | Packets | Priming | Padding |
| --- | --- | --- | --- | --- |
| `lc-mono-48.wav` | 64000 bit/s | 19 | 1024 | 672 |
| `lc-stereo-44.wav` | 128000 bit/s | 17 | 1024 | 67 |
| `lc-pns-48.wav` | 192000 bit/s | 19 | 1024 | 672 |

The source name `lc-pns-48` identifies the shared WAV input; this encoder does not
encode PNS. It uses long sine windows, independent channels and separate scales
for the frequency bands. Bounded local allocation trades reconstruction error
against exact scale/section/spectral bit costs within the frame ceiling. All 55 decoded packet frames are checked in managed and Native AOT
tests, with maximum absolute PCM difference bounded by 0.00002. Block-boundary,
timing, delay and budget tests are separate from perceptual quality assessment.

The three complete source signals have unweighted source SNR of approximately
19.1, 19.6 and 23.3 dB after removing the declared delay and padding. The manifest
records exact measurements and actual raw bitrate. These generated inputs and
this metric do not establish listening quality or transient performance.
