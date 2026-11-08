The arithmetic encoder follows the decision, bypass, carry and termination
processes in [ITU-T H.264, clause 9.3.4](https://www.itu.int/rec/T-REC-H.264-202606-I/en).
It shares the existing probability states and normative range tables with
decoding. Outstanding bits are emitted in bounded words; no source picture,
coefficient array or output copy is retained by the arithmetic engine.

The caller owns the NAL writer. A terminating bin supplies the arithmetic flush,
the stop bit and byte alignment. `H264BitWriter.TryFinishCompletedRbsp` then
publishes that completed RBSP without adding another stop bit. The same engine
termination can precede I_PCM samples and a fresh arithmetic engine, retaining
the probability contexts.

## Arithmetic verification

All 64 probability states, both most-probable symbols and four decision patterns
are checked in 512 mixed sequences. Each includes decisions, zero to 32 bypass
bits, false termination bins and final termination. The resulting code point
must lie inside an independently subdivided arbitrary-precision interval. A
separate streaming decode verifies every bin and final context state. Checks
also cover the initial flush, invalid widths, alignment, terminal-state access
and released packet storage.

## Picture encoding

`H264EncoderOptions.EntropyCoding` selects `Cavlc` (Baseline profile, the default)
or `Cabac` (Main profile). Both use the same prediction, motion search,
quantization and reconstructed-picture kernels. CABAC writes I4x4, P16x16 and
skip macroblocks, luma/chroma coded-block flags and coefficient levels. Probability
states restart at each slice. The encoder retains bounded macroblock metadata
and a two-row motion-difference window; it does not retain another source picture.

`H264BitWriter.TryFinishCabac` applies the normative bin-density byte stuffing
after termination and encapsulation. The complete packet, including stuffing,
participates in the existing rate and level budgets.

Fourteen complete independent references cover 1120 I/P pictures: five quantizers,
cropped edges, horizontal/chroma motion, a scene change, variable frame duration,
IDR intervals, frame-number rollover and constrained rate control. Every decoded
YUV sample matches FFmpeg. The references also verify timing, released storage
and reproducible MPEG-TS output. Existing CAVLC transport hashes are unchanged.

## Initial complete-operation comparison

The same Native AOT executable encodes each input using either entropy mode at
QP26, with one logical CPU and alternating CAVLC/CABAC/CABAC/CAVLC runs. CPU includes
input reads, picture encoding and MPEG-TS writing. These are two runs per mode,
so small timing differences require broader confirmation.

| Input | Pictures | CAVLC video bytes | CABAC video bytes | CAVLC CPU median | CABAC CPU median |
| --- | ---: | ---: | ---: | ---: | ---: |
| Natural video | 288 | 4,139,535 | 2,992,015 | 6.024 s | 6.597 s |
| Motion | 192 | 1,429,276 | 1,289,503 | 0.929 s | 1.200 s |
| Flat content | 192 | 82,464 | 33,312 | 1.777 s | 1.858 s |

PSNR and SSIM are identical between modes for all three complete outputs because
the reconstructed pictures are unchanged. Natural-video PSNR is 43.109928 dB and
SSIM is 0.977395; motion PSNR is 45.633750 dB and SSIM is 0.994388. CABAC reduces
video payload, but this initial implementation increases CPU. Matching x264
`veryfast` across speed, quality and size remains open.

Buffering the decided arithmetic bits in 64-bit words preserves all fourteen
transport and pixel hashes with hardware intrinsics enabled and disabled. A
separate alternating comparison measured about 2.6% less CPU for natural video
and 2.9% less for motion; the flat-content difference was within run variation.
This removes part of the arithmetic output overhead, without changing motion
search or the remaining performance gap.

## Motion-cost storage

Motion search scores both chroma planes together without storing a candidate
prediction. It builds the complete 384-byte prediction once, after choosing the
motion vector. The prepared luma planes supply the final fractional prediction.
Border samples use bounded stack storage; repeated chroma scoring allocates no
managed memory.

Independent bilinear checks cover 660,480 combinations of phases, borders,
extreme vectors and rejection thresholds. Normal, hardware-intrinsics-disabled
and strict Native AOT runs pass. Complete transport and decoded-picture
references remain unchanged. Initial alternating CPU observations vary enough
between runs that they do not establish a new stable speed ratio.
