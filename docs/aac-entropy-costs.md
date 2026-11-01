AAC band quantization selects the spectral Huffman codebook with the smallest
exact bit cost. Selection retains the earlier codebook when costs tie. Zero
bands omit spectral data. Quantizer scales, distortion, channel allocation,
section formation and output syntax retain their existing behavior.

## Shared spectral costs

Codebook pairs 1/2, 3/4, 5/6, 7/8 and 9/10 share their tuple representation.
The selector computes each tuple symbol once for both tables, reads their
individual code lengths and shares the count of nonzero signs for unsigned
tables. One initial scan determines the band's maximum quantized magnitude and
sign count. A pair whose domain cannot represent that maximum requires no tuple
scan. The escape codebook retains its exact sign and escape-bit accounting.

All code lengths are nonnegative. Once both partial costs in a pair reach the
best earlier complete cost, neither can win; counting that pair stops. Strict
comparisons preserve the original ascending-index tie order. Selection uses
existing tables and scalar locals without an additional buffer or lookup table.

The paired-table structure and maximum-magnitude classification also appear in
the AAC reference encoder described by
[ETSI TS 126 410](https://www.etsi.org/deliver/etsi_ts/126400_126499/126410/19.00.00_60/ts_126410v190000p.pdf).
[ETSI TS 126 403, section 5.6.3](https://www.etsi.org/deliver/etsi_ts/126400_126499/126403/18.00.00_60/ts_126403v180000p.pdf)
describes noiseless spectral coding. Mediana implements cost selection in its
managed AAC module.

## Complete ten-minute resource comparison

The input contains 26,460,000 PCM samples per channel: 600 seconds of 44.1 kHz
stereo, encoded through the complete CLI conversion at a 128 kbit/s raw AAC
ceiling. Fresh Linux x64 Native AOT processes use CPU affinity to one logical
CPU and `DOTNET_PROCESSOR_COUNT=1`. Separate short warmups precede four complete
measured runs in before/after/after/before order. No build, test or other media
workload runs concurrently with these measurements.

Each version has two complete observations on the shared host. Medians include
the whole conversion process, including startup, input reading and MPEG-TS output.

| Measurement | Separate codebook scans | Shared paired costs |
| --- | ---: | ---: |
| Individual CPU, seconds | 86.986626 / 86.111922 | 55.998846 / 57.025516 |
| Median CPU, seconds | 86.549274 | 56.512181 |
| Median elapsed, seconds | 88.201075 | 58.031014 |
| Median peak RSS, MiB | 28.000 | 28.000 |
| Individual peak RSS, KiB | 28672 / 28672 | 28800 / 28544 |

Median process CPU decreases by 34.71%, corresponding to 1.53 times the processing
rate for this recording. All four complete MPEG-TS files are byte-identical.
The measurement describes this input and host; broader throughput comparisons
require additional recordings and platforms.

## Exactness and independent decoding

Shared managed and Native AOT checks cover 5,936 spectral tuple/sign cases and
1,188 complete bands. They include zero, sparse, signed, boundary and escaped
magnitudes through 8191, lengths from 4 through 1024, and equal-cost alternatives.
An unchanged exhaustive counter evaluates all eleven codebooks. The selected
book and exact bit count must match, actual written length must equal the count,
and decoding must restore every quantized value.

Fourteen complete public conversions cover mono/stereo signals, unequal and
alternating channels, speech, impulses and five complete EBU SQAM music
recordings. Before/after MPEG-TS bytes match for every input. These streams and
the complete ten-minute output decode independently; all 73,537,536 decoded
Float32 values are finite and agree with Mediana within `1.79e-7` absolute error.
Every access unit's byte-aligned bit ceiling, PTS, DTS and duration is checked.

The long stream contains 25,841 access units and 26,461,184 decoded samples per
channel, including 1024 priming samples and 160 final padding samples. A further
complete conversion with the final executable matches the measured transport.
All 2906 managed tests, 30 locally packed libraries, the isolated package-only
strict Linux x64 Native AOT consumer and complete native test runner pass.
Release build, formatting and root license-header checks pass.
