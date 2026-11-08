# H.264 arithmetic references

## Fixed-QP level burst

`level-burst.sha256` records the independent decoded YUV420P8 checksum for a
640x352 picture containing seeded .NET `Random(172)` bytes in tight Y, U and V
order. Encoding uses QP26, time base 1/172, timestamp zero and duration one tick,
BT.709 limited-range color, left chroma location and sample aspect ratio 4:3.
Its initial VCL picture exceeds 50 Mbit/s multiplied by that one-frame interval;
the initial access-unit and buffer bounds admit the burst.

The shared managed/Native AOT check verifies all 337,920 decoded samples against
FFmpeg 6.1.1, exact input timing and format, deterministic reset and complete
media-buffer release. The MPEG-TS capture is 240,264 bytes with SHA-256
`20aaa63432b574b212f1f0f94f29070fecc7bed07fe6eca1aacc06bc019f35f0`.
The decoded checksum is
`3654a8a454dc0ac5d5daa703821b69ff76c13a3cf59262a2f414392de9c2b36c`.

```sh
ffmpeg -hide_banner -v error -nostdin -i level-burst.ts -map 0:v:0 -threads 1 -pix_fmt yuv420p -fps_mode passthrough -f rawvideo level-burst.yuv
sha256sum level-burst.yuv
```

`cabac-intra-contexts.bin` contains packed initial CABAC states for progressive
I-slice contexts. It follows ITU-T H.264 V15 (08/2024), tables 9-12 and 9-16
through 9-21 and equation 9-5.

The outer order is SliceQPY from 0 through 51. Within each quantizer, context
indices ascend through 3–10, 60–275 and 399–401. Each byte stores
`2 * pStateIdx + valMPS`. There are 227 contexts per quantizer, 11,804 bytes.
The reference was calculated separately with integer floor division, clipping
the intermediate state to 1–126 before packing the probability state and symbol.

SHA-256: `f1eacad024365024b15462922c89aa1ebff91f9071dfb8b67047aece8a93bb77`.

`cabac-predictive-contexts.bin` contains the corresponding progressive P-slice
states under tables 9-13 and 9-15 through 9-21. The outer order is
`cabac_init_idc` 0, 1, 2, then SliceQPY 0 through 51, then context indices
11–23, 40–275 and 399–401. There are 252 contexts per selection and quantizer,
39,312 bytes total. Packing and separate integer calculation follow the same
definition as the I-slice reference.

SHA-256: `6e2bfd1bb2cdd4117e36f6b699fa85eb930a200c087dc092dc74c4bd042bb9fa`.

`cabac-bidirectional-contexts.bin` contains B-specific contexts 24–39 from
table 9-14. The outer order is `cabac_init_idc` 0, 1, 2, then SliceQPY 0–51,
then ascending context index: 2496 bytes, calculated and packed independently
with the same floor division and clipping. B slices share contexts 40–275 and
399–435 with the corresponding P initialization selection. Exact interval checks
cover all 49 B macroblock types, 13 sub-macroblock types, skip contexts and
independent reference-index/motion-difference state for both lists.

SHA-256: `09abac329297c5c371e9828403d284797477fabec58df65ecda8ea693740a086`.

Source: [ITU-T H.264 V15 (08/2024)](https://www.itu.int/rec/T-REC-H.264-202408-S/en).
These are arithmetic initialization references; they do not prove complete
CABAC picture reconstruction.

## B prediction component checks

Managed and Native AOT checks cover independent L0/L1 motion storage and spatial
motion prediction, ordinary averaging, explicit luma/Cb/Cr weights and implicit
POC-based weights under clauses 8.4.2.3 and 8.4.3. Fixed sample vectors include
negative weights, rounded offsets, clipping and both implicit weight limits.
The explicit weight-sum constraint includes the distinct denominator-seven limit.

Partition checks exercise single-list and bidirectional prediction with all three
weight modes. Linear sample ramps verify fractional luma and chroma interpolation;
rectangle checks cover 16x16, 16x8, 8x16, 8x8, 8x4, 4x8 and 4x4 placement.
I/P storage retains its existing byte budget; B storage adds the second motion list.
The component vectors are supplemented by complete CAVLC and CABAC B reconstruction checks
using the MP4 fixtures described in [MP4/MOV fixtures](../Mp4/README.md).
They include B macroblock syntax, spatial/temporal direct prediction, deblocking,
presentation order and PNG previews, including all CABAC initialization selections
and actual B-picture 8x8 transforms.

## Intra 8x8 prediction reference

`intra8-modes.h264` contains nine explicit High-profile 32x32 IDRs, one for each
directional Intra_8x8 mode 0..8. Each picture contains three PCM macroblocks
followed by an Intra_8x8 macroblock with four blocks using the selected mode,
zero residuals and disabled deblocking. PCM samples follow
`(29 * x + 17 * y + 73 * address + 41 * plane) % 256` within each plane block.
The stream uses CAVLC, POC type 2, flat scaling, QP 26 and 8-bit 4:2:0 samples.
Its 8x8 mode bits explicitly select the intended modes; it is not an encoder's
automatic mode selection. Right-edge top references require normative extension.

`intra8-modes.yuv` is the independent decoded output, 13,824 bytes. The prediction
check compares every luma sample in all 36 Intra_8x8 blocks, including filtered
neighbours and the negative-angle branches at nonzero horizontal positions.

```sh
ffmpeg -hide_banner -v warning -nostdin -y -i intra8-modes.h264 -map 0:v:0 -pix_fmt yuv420p -fps_mode passthrough -f rawvideo intra8-modes.yuv
```

| File | SHA-256 |
| --- | --- |
| intra8-modes.h264 | a9fbdf806f1a290cea70a9219cacf2b7f20a85144e334d2be4bded8a20d4e166 |
| intra8-modes.yuv | 9f6b5985c1c38e60be533dc3b31a58a3fe64b759959110221bf2051f8fc8bde5 |

## Progressive CABAC 8x8 references

`cabac-8x8-contexts.bin` contains the 34 initial states for context indices
402..435 from Table 9-24, used by progressive 8x8 significance, last-significant
and coefficient levels. The outer selection order is I, P initialization 0,
P initialization 1, P initialization 2; each selection contains QP 0..51 and
ascending context indices. Packing is `2 * pStateIdx + valMPS`, calculated
separately with signed integer floor division and clipping. Total: 7,072 bytes.

`cabac-8x8-mapping.bin` stores the 63 pairs of progressive significance and
last-significant context increments from Table 9-43, in scan-position order
0..62. Total: 126 bytes. The separate exact-interval writer uses this reference;
the decoder contains its own significance table and last-position ranges.
Checks cover every scan position, dense and sparse blocks, signed-short
coefficient boundaries, I and all three P initializations, and QP 0/25/51.
The 8x8 residual has no separate coded-block flag for 4:2:0 luma.

| File | SHA-256 |
| --- | --- |
| cabac-8x8-contexts.bin | d02aae1ac06cbf564bbd6e2acb7f6c54906e9435bad966d9bd3b43ec612a58cc |
| cabac-8x8-mapping.bin | b85322398d2be8f82c2c98734a0f3ed28f8cb393e11b9181fcb4e9dada543138 |

## CABAC Intra16 reconstruction

`CABAC-INTRA16.json` describes thirty complete two-picture Main-profile streams
and their transport and decoded-pixel hashes. Each stream contains one I and
one P slice, both with four Intra16 macroblocks in prediction modes 2, 1, 0 and 3.
QP 0, 12, 26, 38 and 51 cover gradients, neutral samples, constant planes,
luma gradients and luma/chroma checkerboards. These exercise luma DC/AC and
chroma coded-block patterns, including zero residuals and slice-edge contexts.
Syntax follows H.264 clauses 7.3.5 and 9.3, including the nonterminating PCM
selection bin inside the Intra16 macroblock type.

`cabac-intra16-qp*.yuv.gz` contain all independently decoded 32x32 pictures,
concatenated in ascending content order for each QP. Their uncompressed size
is 18,432 bytes per QP. FFmpeg 9.0.2 decoded all sixty pictures without errors;
every sample matches encoder reconstruction. Full-range samples are preserved
without scaling, range conversion or frame-rate duplication:

```sh
ffmpeg -nostdin -v error -threads 1 -err_detect explode -i input.ts -map 0:v:0 -fps_mode passthrough -c:v rawvideo -pix_fmt yuvj420p -f rawvideo output.yuv
```

The checks use the same post-analysis coefficient and reconstruction kernel for
CAVLC and CABAC. Automatic encoder mode selection is tested separately.
