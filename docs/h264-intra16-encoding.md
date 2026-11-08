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
