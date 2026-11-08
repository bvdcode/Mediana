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
references remain unchanged.

A subsequent comparison runs six complete Native AOT processes per variant
and input, in alternating ABBA/BAAB/ABBA order on one logical CPU. Input reading,
encoding and MPEG-TS writing are included. Every complete transport hash
matches the original reference.

| Input | Previous CPU median | Direct-cost CPU median | Change |
| --- | ---: | ---: | ---: |
| Natural video | 5.920 s | 5.283 s | −10.8% |
| Motion | 1.073 s | 0.973 s | −9.4% |
| Flat content | 1.659 s | 1.574 s | −5.1% |

Median peak resident memory is approximately unchanged: 51.0/51.1 MB for natural video,
13.9/14.0 MB for motion and 51.8/51.8 MB for flat content. Elapsed time contains
host scheduling outliers; CPU also has outliers on motion and flat content.
Natural video gives the most consistent CPU reduction. These figures compare
process CPU medians. They are
an internal before/after comparison and do not establish parity with x264.

## Current x264 comparison

A subsequent comparison measures complete Y4M input, H.264 encoding and MPEG-TS
output with one logical CPU. Each configuration runs six fresh processes in
ABBA/BAAB/ABBA order. Mediana uses the direct-cost Native AOT build at QP26;
FFmpeg 6.1.1 uses libx264 with its normal assembly implementation. The two
measured reference QPs bracket Mediana's decoded PSNR; nominal QP equality is
not treated as equal quality. All 144 timed processes complete successfully,
and quality checks compare all 4,512 pictures against the original planes.

| Input | Mediana PSNR | Common-tool x264 CPU ratio | Default veryfast CPU ratio |
| --- | ---: | ---: | ---: |
| Natural video | 43.110 dB | 4.56–4.64× | 2.82–2.84× |
| Motion | 45.634 dB | 3.37–3.69× | 2.23–2.54× |
| Flat content | 53.969 dB | about 4.30× | 2.82–2.87× |

Ratios are Mediana process CPU divided by x264 process CPU. The common-tool
configuration uses Main profile, CABAC, no B pictures, one reference, a 48-picture
GOP and disabled scene cuts. Default veryfast retains its High profile, B pictures
and adaptive GOP; its results are reported separately. Both include their
respective muxer and process overhead.

| Input | Mediana AVC bytes | Common-tool x264 AVC bytes | Default veryfast AVC bytes |
| --- | ---: | ---: | ---: |
| Natural video | 2,993,947 | 1,195,638–1,318,137 | 911,184–1,011,813 |
| Motion | 1,290,795 | 918,035–984,556 | 811,356–878,739 |
| Flat content | 34,600 | 24,386–24,894 | 12,708–12,828 |

These are complete Annex-B video bytes, including configuration and framing,
at the same measured endpoints. Median peak process RSS is approximately
49/21/50 MiB for Mediana on natural/motion/flat inputs, 79–80/57/75–76 MiB
for common-tool x264 and 102–103/64/100–102 MiB for default veryfast.
Lower memory does not compensate for the remaining speed and size differences.

Natural-video reference PSNR spans 42.795–43.277 dB for common tools and
42.870–43.347 dB for defaults. Motion spans 45.358–46.304 dB and
44.954–45.962 dB respectively. Flat content has a nonmonotonic quality curve:
dominated points are excluded, and the remaining bracket is wide,
53.560–56.443 dB or 53.560–56.377 dB. These are actual measured endpoints,
not an exact equal-quality CPU interpolation or proof of equal perceptual quality.

Individual CPU coefficients of variation reach 20–31%; alternating four-run
blocks also show a multiple-fold gap. The measurements do not establish a
universal coefficient with percent-level precision. This covers three
8–12 second inputs on Linux x64; the speed and compression-efficiency targets
remain open.
