# H.264 constant residuals

A 4x4 residual block with no AC coefficients has the same reconstructed residual
at every sample. Mediana detects this case before the two inverse-transform
passes and fills the destination with the rounded DC value. Blocks containing
any nonzero AC coefficient retain the general transform.

The result follows from [ITU-T H.264 (08/2024)](https://www.itu.int/rec/recommendation.asp?lang=en&parent=T-REC-H.264-202408-S),
clause 8.5.12.2, equations 8-338 through 8-354. With only `d00` nonzero, the two
inverse passes produce `d00` at every position, and every output sample is
`floor((d00 + 32) / 64)`. Separately scaled DC coefficients are used directly;
ordinary DC coefficients receive the existing quantizer-dependent scale first.

The common transform still validates the quantizer and complete input/output
spans before writing. Scaling uses 64-bit intermediate arithmetic, and narrowing
to an output integer remains checked. The operation adds no heap buffer or
workspace. The zero-AC scan and constant fill use BCL span operations.

## Correctness

An independent exact-integer oracle checks 2,184 DC cases over all 52 quantizers,
both DC representations, signed rounding boundaries and integer overflow. The
oracle uses arbitrary-precision multiplication and division. Another 120 cases
cover every AC position at both quantizer extremes, and six overlapping-storage
layouts check the output and untouched surrounding memory. Invalid quantizers
and incomplete spans remain rejected.

Both transforms pass these checks, including the 19 inverse-transform cases with
hardware intrinsics disabled. The checks also run through Native AOT.

## Complete-file measurement

The original [Big Buck Bunny 720p source](real-hd.md) was decoded through EOF in
separate Native AOT processes before and after the change. Each process hashed
all visible YUV data and checked every picture hash, PTS and duration against an
independent reference. A fresh full FFmpeg decode supplies a second reference.

Each run covers 14,315 pictures, 596.458333 seconds of video and 19,789,056,000
visible decoded bytes. The Native consumers also write a per-picture CSV;
comparison requires identical complete CSV files and no retained media storage
at EOF.

Linux x64, Intel Core i5-12450H, .NET 10.0.13 Native AOT and FFmpeg
6.1.1-3ubuntu5. Processes run sequentially on the same logical CPU, with one
FFmpeg decoder and output thread. CPU is user plus system time; memory is process
peak RSS. There is one full-file observation per version on a shared host.

The operation includes container reading, SHA256 hashing and per-picture output.
The Native consumers additionally check reference hashes and exact times while
decoding. This measures complete decoding rather than encoding.

| Decode and hash all 14,315 pictures | CPU | Elapsed | Peak RSS |
| --- | ---: | ---: | ---: |
| Mediana before (`eb0a420`) | 416.47 s | 418.95 s | 106.4 MiB |
| Mediana after (`b9728df`) | 384.15 s | 388.88 s | 106.3 MiB |
| FFmpeg | 103.83 s | 104.22 s | 76.6 MiB |

The observed CPU reduction is 7.76%, a 1.08-fold speedup.
Peak RSS was practically unchanged. The optimized complete operation still uses
3.70 times FFmpeg's CPU on this input. Both Native AOT outputs reached EOF,
released all media storage and matched every picture hash and exact timestamp
from both independent decodes. Their complete CSV files were identical.
