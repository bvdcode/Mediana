# H.264 macroblock mode decision measurements

A complete trial of adaptive intra coding in P pictures produced valid streams,
but required 2.08 times the CPU for a 0.54 dB aggregate PSNR improvement on the
measured HD input. It was not adopted. The encoder continues to use P16x16 and
P_Skip between IDR pictures.

## Coding model

For every non-skip P macroblock, the trial compared P16x16 with I4x4 using
reconstructed Y, U and V squared sample error plus a quantizer-dependent RBSP
bit cost. Intra4x4 analysis, residual transforms, chroma prediction and CAVLC were
shared with IDR coding. P slices used `mb_type=5` for I4x4, with the existing
I_PCM replacement retained for the 3200-bit macroblock limit.

The P/I macroblock syntax follows H.264 clause 7.4.5 and Tables 7-11 and 7-13.
For P slices, values 5 through 30 select the I table after subtracting 5.
See [ITU-T H.264, August 2021](https://www.itu.int/rec/dologin_pub.asp?id=T-REC-H.264-202108-S!!PDF-E&lang=e&type=items).

The distortion penalty used a Q8-rounded form of
`lambda = 0.85 * 2^((QP - 12) / 3)`. This core relation is present in the
[JM reference encoder's lambda calculation](https://vcgit.hhi.fraunhofer.de/jvet/JM/-/blob/master/source/app/lencod/lambda.c).
The trial used unweighted reconstructed YUV error and did not reproduce JM's
configuration-dependent scaling or complete mode search. This is an encoder
decision model, not a normative H.264 requirement or a perceptual quality metric.

Each candidate used the current macroblock's retained prediction and coefficients.
The losing inter candidate was rewritten after restoring the bit position.
No additional picture-sized pixel backup was introduced. PCM replacement refreshed
the macroblock checkpoint so later candidate rollback preserved the bit writer's
revision checks.

## Complete HD operation

The input was the complete 60.041667-second, 1441-picture, 1280x720, 24 fps
movie excerpt used for the fractional-motion comparison. Both fresh Linux x64
Native AOT processes used the same CPU affinity, initial QP26, 10 Mbit/s packet
ceiling, two-second buffer, two-second IDR interval and video-only MPEG-TS output.
The measurement includes input decoding and output writing.

| Complete conversion | Current encoder | Exhaustive P/I trial |
|---|---:|---:|
| CPU seconds | 162.85 | 339.38 |
| Wall seconds | 167.74 | 345.80 |
| Peak RSS, MiB | 104.57 | 101.52 |
| MPEG-TS bytes | 78,295,608 | 78,010,976 |
| Aggregate YUV PSNR, dB | 44.9564 | 45.4952 |

CPU increased by 108.41%; transport size decreased by 0.36%. These are one
sequential pair on a shared host. The RSS observations do not establish a general
memory reduction, and the result does not establish perceptual quality or an
external-encoder speed comparison.

## Verification

Both full outputs passed independent checks of every packet timestamp, duration,
all 31 IDR boundaries and all packet-rate windows. Independent decoding and a
fresh Native AOT public consumer agreed on all 1,992,038,400 visible sample bytes
per output, reached EOF and retained no media buffers. Canonical MP4 remuxing
preserved every coded picture. The level-window checks passed; complete Annex C
HRD conformance remains open.

Twenty-one short sequences, including moving texture, a scene change, cropped
edges, low-QP PCM blocks, skips and controlled rate budgets, matched independent
decoding for all 2,873,448 reconstructed sample bytes. One CLI conversion also
produced identical managed and Native AOT transport bytes. Fifteen direct mode
cases at QP0, 12, 26, 38 and 51 selected intra blocks after a scene change and
compact skips for the repeated picture, preserving the original reference.

The short rate-constrained cases included a 0.98 dB PSNR regression and a 1.61 dB
improvement. A lower local rate/distortion cost does not guarantee lower error
over a complete rate-controlled sequence. Adaptive intra coding in P pictures
remains open; an efficient decision mechanism needs to avoid exhaustive
transform and entropy trials while preserving independent output verification.
