The intra16 encoder writes CAVLC luma DC and AC coefficients, all four 16x16
luma prediction modes, chroma residuals and quantizer transitions in I and P
slices. It shares chroma quantization, residual writing and reconstruction with
the existing encoder. Working coefficients and predictions use bounded stack
storage; no additional picture buffer is retained.

The luma DC path applies a separable 4x4 Hadamard transform to the raster DC
coefficients before quantization and zigzag scanning. An independent direct
matrix calculation checks 2,080 cases across every quantizer. Macroblock syntax
and inverse reconstruction follow clauses 7.4.5 and 8.5 of
[ITU-T H.264](https://www.itu.int/rec/T-REC-H.264-202606-I/en).

Two-picture 32x32 references exercise an IDR followed by a P picture, including
DC, horizontal, vertical and plane predictions. Every reconstructed Y, U and V
sample matches FFmpeg 6.1.1 decoding. Verification preserves the declared full
sample range and original frame timestamps; no frame-rate or range conversion
is applied. Each reference contains 3,072 sample bytes and reaches EOF with no
retained media storage.

| QP | Decoded reference SHA256 |
| --- | --- |
| 0 | `38d77639842e33df10ee5bc1040ba2639743424c0c0e239fa129a7168e6949a9` |
| 12 | `9ec893480510a663e6b6477d610e015e853e56e8d4dc79a013fd6da9a93447da` |
| 26 | `91c97dc18a215903350b0c19bd5193f2c7e83b5be7aae3697f614ae25c22ed58` |
| 38 | `0b567bba3a9155aa8845ee99bf76f81a0e23303ab4fbbf50295311ecb21769a1` |
| 51 | `2328e1d4bfb70f46ad2bb30386d538c056bfbc310c9914816becdf91ad6c198c` |

The references are `intra16-qp*.yuv` under the H.264 test data. These checks
establish coefficient and syntax correctness. Selection against intra4x4 and
inter modes, and its joint effect on CPU, size and quality, require separate
complete-sequence comparisons.

## Mode selection comparisons

Two P-picture policies were measured with Linux x64 Native AOT on complete
12-second natural video, eight-second moving patterns and eight-second static
graphics. Each ran in before/after/after/before order with one processing thread,
QP26 and 48-picture IDR spacing. Independent decoding measured PSNR and SSIM
against the same raw source. These shared-host pilot pairs are not matched-rate
comparisons with another encoder.

The first policy screened all four intra16 predictions by luma SAD and a syntax
penalty, then compared reconstructed YUV squared error plus actual coded bits
against the completed inter candidate. The second selected the screened mode
directly, avoiding duplicate transform/entropy work.

| Material | Policy | Median CPU, before / after, s | AVC payload, before / after, bytes | PSNR, before / after, dB |
| --- | --- | ---: | ---: | ---: |
| Natural video, 1280×720, 288 pictures | Complete reconstruction cost | 5.312 / 6.818 | 4,139,535 / 2,728,224 | 43.110 / 43.582 |
| Moving patterns, 640×360, 192 pictures | Complete reconstruction cost | 0.839 / 0.921 | 1,429,276 / 1,407,796 | 45.634 / 46.030 |
| Static graphics, 1280×720, 192 pictures | Complete reconstruction cost | 1.658 / 1.730 | 82,464 / 82,464 | 53.969 / 53.969 |
| Natural video | Direct screened selection | 5.399 / 7.237 | 4,139,535 / 2,734,182 | 43.110 / 43.589 |
| Moving patterns | Direct screened selection | 0.874 / 0.937 | 1,429,276 / 1,413,933 | 45.634 / 46.143 |
| Static graphics | Direct screened selection | 1.794 / 2.168 | 82,464 / 82,464 | 53.969 / 53.969 |

The first policy reduced natural-video payload by 34.1% and improved PSNR by
0.47 dB, but added 28.3% CPU in this pilot. Direct screening did not establish a
speed improvement. Row-copy/fill prediction produced identical encoded bytes
in a further pair but did not remove the complete-operation CPU increase.

The direct policy also passed complete independent pixel reconstruction on
twelve 80-picture sequences covering scene changes, moving texture, cropped
edges, low/high QP and rate control. Source hashes match the existing temporal
and rate-control references. The rate-controlled QP0 sequence at 32 kbit/s lost
1.226 dB, despite improvements in several other cases. Neither policy was adopted
as the default mode decision. The coefficient/syntax component remains available
for further encoder work; these pilots establish no throughput or memory win.
