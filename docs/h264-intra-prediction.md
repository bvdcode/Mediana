IDR luma encoding selects among the nine H.264 intra4x4 prediction directions.
The input remains progressive, even-sized YUV420P8 with explicit source color,
sample geometry and exact packet timing; public encoder options are unchanged.

## Direction selection and syntax

The encoder reads reconstructed top, left and corner samples once for each
4x4 block. Only directions whose required neighbours are available are examined.
Unavailable top-right references use the same normative edge extension as the
decoder. The available choices are vertical, horizontal, DC, diagonal down-left,
diagonal down-right, vertical-right, horizontal-down, vertical-left and horizontal-up.

Selection minimizes absolute prediction error across all 16 source luma samples.
Equal errors prefer the neighbouring-mode predictor, which needs one flag bit.
Other selected directions use that flag and the three-bit remaining-mode code.
Quantization and reconstruction follow selection; the next block uses those
reconstructed samples. Source samples never replace reconstructed references.

One macroblock's quantized coefficients are retained until its prediction and
coded-block-pattern syntax can be written in the required order. Zero luma groups,
zero chroma groups and unnecessary `mb_qp_delta` are omitted. The residual pattern
uses the decoder's shared intra mapping table. Chroma prediction remains DC.
There is no extra retained picture or frame queue. Existing reference lifetime,
input borrowing, reset, cancellation and output timing contracts remain in force.

Prediction and emitted syntax follow
[ITU-T H.264 V15](https://www.itu.int/rec/T-REC-H.264-202408-S/en),
clauses 7.3.5.1 and 8.3.1.1–2 and Table 9-4. The search objective is an encoder
decision. It does not minimize coded bit cost or quantized reconstruction error
and does not establish a perceptual optimum. QP0 still uses quantization.

## Complete verification

Nine exact directional patterns use independently evaluated normative sample
formulas. Tests cover all 72 combinations of direction and neighbour availability,
invalid modes, unchanged reference samples and zero retained storage. An exact
flat picture checks every macroblock's mode flags and zero residual pattern,
including omission of QP deltas and exact trailing bits; reconstruction remains
128 in every Y/U/V sample.

Seventeen complete sequences contain 1038 pictures and 2,762,184 visible YUV
samples. They cover all-IDR input, temporal I/P, static content, translation,
chroma motion, scene changes, cropped dimensions, non-square pixels, variable
cadence, negative origins, frame-number rollover and fixed/rate-controlled
QP0/18/26/38/51. Every sample matches FFmpeg 6.1.1's independent H.264 decoder
exactly. A separate complete 18-picture CLI scaling reference is also independently
decoded. Source pixels and source hashes remain unchanged.

Managed and strict Linux x64 Native AOT test runners check the complete references,
packet hashes, timing, HLS boundaries, rate budgets and resource ownership. The full
managed suite passes 2544 tests; Release build, formatting, license headers and
strict Native AOT CLI/test builds pass.

## Error and transport-size comparison

The following complete outputs compare DC-only luma prediction with directional
selection and actual residual patterns. All sizes include MPEG-TS overhead.
PSNR uses every visible 8-bit Y/U/V sample relative to its decoded source.
Fixed QP does not promise equal error between encoding decisions.

| Source and setting | TS bytes before | TS bytes after | PSNR before / after (dB) |
| --- | ---: | ---: | ---: |
| Static, QP26 | 25,568 | 25,568 | 43.61 / 43.61 |
| Horizontal motion, QP26 | 32,712 | 32,148 | 41.62 / 40.67 |
| Chroma motion, QP38 | 25,944 | 25,944 | 32.23 / 32.23 |
| Vertical motion and scene change, QP18 | 74,824 | 74,260 | 47.49 / 46.93 |
| Horizontal motion, QP0 | 58,656 | 57,528 | 75.35 / 69.10 |
| Horizontal motion, QP51 | 25,944 | 25,944 | 21.90 / 22.62 |
| Static, initial QP26, 8 kbit/s | 25,568 | 25,568 | 59.63 / 59.63 |
| Horizontal motion, initial QP26, 32 kbit/s | 33,088 | 33,088 | 35.38 / 37.35 |
| Chroma motion, initial QP26, 48 kbit/s | 34,404 | 34,404 | 58.57 / 58.57 |
| Vertical motion and scene change, initial QP26, 40 kbit/s | 49,256 | 49,256 | 29.38 / 30.13 |
| Horizontal motion, initial QP0, 32 kbit/s | 32,148 | 32,148 | 35.54 / 37.26 |
| Vertical motion and scene change, initial QP51, 40 kbit/s | 46,624 | 47,752 | 25.53 / 25.89 |
| AVC multislice CABAC source, all-IDR QP26 | 75,388 | 69,372 | 40.22 / 39.93 |
| AVC multislice CAVLC source, all-IDR QP38 | 51,700 | 49,820 | 30.10 / 29.81 |
| AVC weighted source, all-IDR QP18 | 33,652 | 29,328 | 49.23 / 49.12 |

Two complete scaled all-IDR references also match independent pixels. At 48x40,
QP26 PSNR changes from 37.76 to 37.58 dB; at 48x30, from 39.25 to 39.19 dB.
These small sequences demonstrate compression/error tradeoffs and reconstruction
correctness. A separate [complete HD re-encoding comparison](h264-hd-encoding.md)
records CPU, peak RSS, every-picture source error and the remaining gap to
libx264. Perceptual acceptance and broader source coverage remain open.
