The existing original WAV fixtures are encoded by Mediana's AAC-LC encoder and
decoded independently by FFmpeg 6.1.1. `ENCODING.json` records complete commands
and SHA-256 hashes. Float references use little-endian interleaved Float32 and
retain every 1024-sample frame, including analysis priming and final zero padding.

| Source | Raw bit ceiling | Packets | Priming | Padding |
| --- | --- | --- | --- | --- |
| `lc-mono-48.wav` | 64000 bit/s | 19 | 1024 | 672 |
| `lc-stereo-44.wav` | 128000 bit/s | 17 | 1024 | 67 |
| `lc-pns-48.wav` | 192000 bit/s | 19 | 1024 | 672 |
| `channel-budget.wav` | 96000 bit/s | 107 | 1024 | 507 |

The source name `lc-pns-48` identifies the shared WAV input; this encoder does not
encode PNS. It uses long sine windows and separate scales for the frequency bands.
Stereo channels share one element budget, preserving independent gain, scale
differences and sections. Bounded local allocation trades reconstruction error
against exact scale/section/spectral bit costs within the frame ceiling. All 162 decoded packet frames are checked in managed and Native AOT
tests, with maximum absolute PCM difference bounded by 0.00002. Block-boundary,
timing, delay and budget tests are separate from perceptual quality assessment.

The first three complete source signals have unweighted source SNR of approximately
19.1, 19.7 and 23.2 dB after removing the declared delay and padding. The manifest
records exact measurements and actual raw bitrate. These generated inputs and
this metric do not establish listening quality or transient performance.

`channel-budget.wav` is an original 48000 Hz PCM16 stereo sequence with 108037
samples per channel. Seven sinusoidal components use frequencies 157, 311, 617,
1223, 2441, 4871 and 9743 Hz, phase `index * 0.19`, amplitude 0.078 each, and
96-sample endpoint ramps. Samples 0–35999 use only the left channel; samples
36000–71999 give the right channel 5% amplitude; the remainder alternates active
channels every 12000 samples. Samples are rounded after multiplication by 32767.
Its complete source SNR is 39.76 dB at 94.125 kbit/s actual raw bitrate. This
reference checks channel activity changes together with every priming and tail
sample, rather than only isolated spectral bands.
