# WebP encoding comparison

This standalone tool compares `Mediana.Codecs.Webp` with ImageSharp 4.1.3,
provided through `EasyExtensions.Drawing` 3.0.95. These packages are benchmark
inputs only; Mediana's WebP runtime module does not reference them.

## Reproduce

```sh
dotnet build benchmarks/Mediana.WebpBenchmarks -c Release
DOTNET_PROCESSOR_COUNT=2 DOTNET_TieredCompilation=0 dotnet benchmarks/Mediana.WebpBenchmarks/bin/Release/net10.0/Mediana.WebpBenchmarks.dll mediana gradient 1024 1024 0 artifacts/webp
```

The six arguments are encoder, sample, width, height, iteration count and output
directory. Lossless encoders are `mediana`, `imagesharp` and `imagesharp-fast`;
lossy encoders are `mediana-lossy`, `imagesharp-lossy` and `imagesharp-lossy-fast`.
Lossy timing runs use quality 75. Samples are
`flat`, `gradient`, `alpha`, `noise`, or an existing image path. File dimensions
replace the width/height arguments. Zero iterations selects approximately three
seconds of work, bounded to 3–1,000 iterations. Run each combination three times
in separate processes, sequentially, with the same environment.

`verify` instead writes a Mediana WebP and its original RGBA, decodes it with
ImageSharp and compares every component. `verify-animation` checks three distinct
frames, exact RGBA, frame durations and repeat count through ImageSharp. Verification
files go to the specified directory; file I/O is excluded from timing runs.

`export-lossy`, `export-imagesharp-lossy` and `export-imagesharp-lossy-fast` write
`output.webp` and `input.rgba`; the fifth argument selects quality instead of an
iteration count. `verify-lossy` additionally decodes through ImageSharp and checks
dimensions and exact alpha, reporting RGB PSNR. `inspect-animation` reads an
existing animation and exports its composited RGBA frames and timing summary.
`export-mixed-animation` creates a 6×6 three-frame fixture combining VP8 and VP8L,
partial rectangles, alpha blending and background disposal.

For independent lossy decoding, export a file and its original RGBA, then decode
the WebP with FFmpeg instead of the benchmark's ImageSharp verifier:

```sh
dotnet benchmarks/Mediana.WebpBenchmarks/bin/Release/net10.0/Mediana.WebpBenchmarks.dll export-lossy gradient 1024 1024 75 artifacts/webp/check
ffmpeg -v error -i artifacts/webp/check/output.webp -pix_fmt rgba -f rawvideo artifacts/webp/check/decoded.rgba
```

Compare alpha bytes directly and compute RGB PSNR as `10 * log10(255² / MSE)`
using the other three bytes per pixel from `input.rgba` and `decoded.rgba`.

## Method

Recorded on 2026-11-05, Ubuntu 24.04 x64, Intel Core i5-12450H, .NET 10.0.14,
SDK 10.0.403. All processes used `DOTNET_PROCESSOR_COUNT=2` and
`DOTNET_TieredCompilation=0`. Mediana encoding is synchronous; ImageSharp's
configuration uses `MaxDegreeOfParallelism=1`. Both run on the same host; CPU
frequency and other host activity are not controlled. Results are medians of
three process runs. The [63 initial lossless measurements](results.json) retain
individual timings before compact token storage; newer measurements are listed
separately below.

Lossless encoders preserve RGB under zero alpha.
ImageSharp uses `Quality=75`, `NearLossless=false`,
`TransparentColorMode=Preserve`, with `Method=Default` (4) or `Fastest` (0).
Mediana has no lossless effort selector. Faster encoding with larger files
is a different compression tradeoff, rather than equal compression efficiency.

Each process prepares both libraries' source images before measurement, warms
up for at least 500 ms and three calls, then collects garbage before timing.
CPU is process CPU time; wall time includes collections during encoding. Allocated
bytes are `GC.GetTotalAllocatedBytes` deltas per image, including encoding output
and temporary objects, excluding input construction. ImageSharp can reuse pooled
storage across calls. Mediana returns a complete owned file and disposes it per
iteration; ImageSharp writes to a counting stream that discards bytes. The latter
does not pay for a destination file buffer. Neither path measures disk I/O.

Peak RSS is the entire process lifetime maximum, including runtime, both source
representations, warmup and encoding. It is not isolated encoder workspace or
live managed memory. Allocated bytes are cumulative allocation traffic, not peak
live memory. These two columns must not be substituted for each other.

Synthetic samples are 1024×1024. Photograph files are the original PNGs from
Google's [WebP gallery](https://developers.google.com/speed/webp/gallery):

| Local sample name | Source | Dimensions | SHA-256 |
| --- | --- | --- | --- |
| photo.png | https://www.gstatic.com/webp/gallery/1.png | 550×368 | a391517dc3962aea18169a1da0dc2c2114dec2e04858eaef33892d0a70452e0d |
| photo2.png | https://www.gstatic.com/webp/gallery/2.png | 550×404 | 968d042312dc01e8feefd5f799b62367fa310eea8d61df6f2c6f831034445a3b |
| photo3.png | https://www.gstatic.com/webp/gallery/3.png | 1280×720 | 3b096c425c9169e95d76e523f5c22981553c88a4b498e9fa3829b2a0d286e465 |

Images are downloaded separately and are not needed to build or run the
synthetic cases.

## Initial lossless measurements

These measurements use revision `9012cf1`. The current encoder produces the same
lossless file lengths for these samples while reducing token storage; see the
allocation comparison below.

| Sample | Encoder | CPU ms | Wall ms | Allocated MiB | Process peak RSS MiB | Output bytes |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| flat | mediana | 0.17 | 0.17 | 0.04 | 67.1 | 32 |
| flat | imagesharp | 19.64 | 19.23 | 12.62 | 88.5 | 88 |
| flat | imagesharp-fast | 56.46 | 56.11 | 12.45 | 86.6 | 88 |
| gradient | mediana | 7.81 | 7.68 | 5.79 | 97.5 | 24,900 |
| gradient | imagesharp | 315.93 | 315.23 | 10.53 | 107.1 | 5,666 |
| gradient | imagesharp-fast | 93.37 | 92.97 | 10.34 | 95.5 | 5,276 |
| alpha | mediana | 10.79 | 10.62 | 5.32 | 87.0 | 29,002 |
| alpha | imagesharp | 328.75 | 328.15 | 10.63 | 110.0 | 2,778 |
| alpha | imagesharp-fast | 128.82 | 128.61 | 10.34 | 98.1 | 9,616 |
| noise | mediana | 81.51 | 82.27 | 16.15 | 103.0 | 3,146,196 |
| noise | imagesharp | 278.81 | 278.22 | 28.49 | 163.7 | 3,145,808 |
| noise | imagesharp-fast | 257.75 | 257.38 | 28.27 | 165.8 | 3,145,808 |
| photo.png | mediana | 12.14 | 12.00 | 3.75 | 67.6 | 347,704 |
| photo.png | imagesharp | 164.96 | 164.34 | 2.48 | 76.8 | 306,030 |
| photo.png | imagesharp-fast | 55.32 | 55.29 | 2.30 | 77.7 | 346,974 |
| photo2.png | mediana | 12.36 | 12.23 | 4.15 | 68.6 | 448,130 |
| photo2.png | imagesharp | 202.61 | 201.95 | 2.65 | 86.4 | 415,088 |
| photo2.png | imagesharp-fast | 57.53 | 57.46 | 2.39 | 80.6 | 442,634 |
| photo3.png | mediana | 54.36 | 54.14 | 12.78 | 99.3 | 1,416,978 |
| photo3.png | imagesharp | 554.55 | 554.03 | 9.34 | 114.0 | 1,196,618 |
| photo3.png | imagesharp-fast | 300.11 | 299.89 | 9.18 | 100.6 | 1,267,796 |

On these three photographs, Mediana takes 10.2–16.4 times less CPU than
ImageSharp Default, or 4.6–5.5 times less than Fastest. Its files are 8.0–18.4%
larger than Default and 0.2–11.8% larger than Fastest. Managed allocation traffic
on photographs is higher in Mediana; whole-process peak RSS is lower in these
runs, sometimes only slightly.

The gradient and alpha patterns expose remaining compression work: their files
are 4.4 and 10.4 times larger than ImageSharp Default, respectively. The noise
case has essentially equal file sizes. Uniform-image encoding benefits from a
specialized path with no full-image transform allocation. These results do not
establish a universal speed or memory advantage, and animation performance has
not been benchmarked.

## Isolated hardware-intrinsics comparison

The same lossless encoder and executable were measured with
`DOTNET_EnableHWIntrinsic=1` and `0`, keeping all other settings above. Three
process pairs per sample alternated their order between repetitions. This changes
both explicit Vector128/Vector256 operations and BCL intrinsic implementations.
Output lengths were identical across modes. The
[30 raw measurements](simd-results.json) use the lossless implementation at
revision `9012cf1`. CPU medians:

| Sample | CPU without intrinsics, ms | CPU with intrinsics, ms | CPU reduction |
| --- | ---: | ---: | ---: |
| gradient | 10.91 | 7.66 | 29.8% |
| alpha | 15.93 | 9.49 | 40.4% |
| noise | 59.45 | 53.68 | 9.7% |
| photo.png | 11.47 | 11.28 | 1.7% |
| photo3.png | 59.09 | 56.22 | 4.9% |

The photographic gain is small relative to run-to-run variation. SIMD has a
clearer effect on predictable synthetic residuals; these results do not attribute
the complete ImageSharp comparison ratio to SIMD.

## Current VP8 lossy measurements

The [72 current measurements](encoding-results.json) use revision `eb4bc0b`: six
samples, four encoder modes and three runs each. These include current lossless
Mediana runs for the allocation comparison below. Lossy modes all use Q75;
ImageSharp uses Default or Fastest, and Mediana uses its directional prediction
and quantization path. An equal quality number is not an equal-quality guarantee.

[Independent RGB quality measurements](lossy-quality-results.json) compare the
original RGBA with FFmpeg-decoded output. PSNR is computed from mean squared error
over all three RGB components, including pixels under zero alpha; higher values
mean lower squared error. Every measured alpha channel is exact. No quality score
was recorded for the noise timing case. The three photographs have similar RGB
PSNR across encoders, while their file sizes differ. PSNR does not replace visual
assessment or establish equal perceptual quality.

| Sample | Encoder | CPU ms | Allocated MiB | Process peak RSS MiB | Output bytes | RGB PSNR dB |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| gradient | mediana-lossy | 43.18 | 0.07 | 62.9 | 19,376 | 41.97 |
| gradient | imagesharp-lossy | 246.97 | 55.22 | 70.7 | 5,720 | 44.16 |
| gradient | imagesharp-lossy-fast | 18.41 | 0.77 | 72.4 | 9,272 | 43.37 |
| alpha | mediana-lossy | 54.75 | 5.30 | 108.9 | 87,550 | 24.10 |
| alpha | imagesharp-lossy | 485.75 | 66.66 | 116.6 | 30,658 | 23.50 |
| alpha | imagesharp-lossy-fast | 132.89 | 12.28 | 111.8 | 31,400 | 23.88 |
| noise | mediana-lossy | 143.02 | 2.10 | 88.0 | 1,073,610 | — |
| noise | imagesharp-lossy | 666.96 | 57.39 | 79.5 | 647,440 | — |
| noise | imagesharp-lossy-fast | 78.83 | 2.69 | 95.3 | 641,786 | — |
| photo.png | mediana-lossy | 13.30 | 0.10 | 60.1 | 44,442 | 32.04 |
| photo.png | imagesharp-lossy | 106.79 | 10.98 | 61.9 | 33,148 | 31.95 |
| photo.png | imagesharp-lossy-fast | 7.62 | 0.32 | 62.1 | 40,596 | 32.14 |
| photo2.png | mediana-lossy | 19.34 | 0.21 | 67.9 | 98,488 | 31.11 |
| photo2.png | imagesharp-lossy | 135.08 | 12.54 | 62.1 | 76,364 | 31.03 |
| photo2.png | imagesharp-lossy-fast | 12.35 | 0.43 | 61.9 | 80,650 | 31.15 |
| photo3.png | mediana-lossy | 75.71 | 0.65 | 77.2 | 318,052 | 32.27 |
| photo3.png | imagesharp-lossy | 534.80 | 49.45 | 72.4 | 257,394 | 32.20 |
| photo3.png | imagesharp-lossy-fast | 43.88 | 1.36 | 93.1 | 279,008 | 32.34 |

On these photographs, Mediana uses 7.0–8.0 times less CPU than
ImageSharp Default. ImageSharp Fastest uses 1.6–1.7 times less CPU than Mediana;
Mediana files are 9.5–22.1% larger than Fastest. This is not a universal
performance lead. Lossless and lossy results describe different encoding paths.

## Lossless token storage before and after

The original token representation used eight bytes for each literal or reference.
The current representation uses one 32-bit value and one reference flag bit for a
literal, or two 32-bit values for a reference. Allocated bytes below include output
and other temporary storage, so their reduction is smaller than the literal-token
reduction. All six lossless output lengths are unchanged between these recordings.
The measurements use the initial baseline and current lossless runs above.

| Sample | Original allocated MiB | Current allocated MiB | Reduction |
| --- | ---: | ---: | ---: |
| gradient | 5.79 | 5.75 | 0.7% |
| alpha | 5.32 | 5.26 | 1.1% |
| noise | 16.15 | 12.26 | 24.1% |
| photo.png | 3.75 | 3.04 | 18.9% |
| photo2.png | 4.15 | 3.32 | 20.0% |
| photo3.png | 12.78 | 9.60 | 24.9% |

The synthetic gradient and alpha compression gaps in the initial lossless table
remain open; compact storage reduces allocation without improving those file sizes.

## Independent decoding

FFmpeg decoded 84 Mediana VP8 images at Q0/Q75/Q100 with exact alpha and no decoder
errors. libwebp 1.6.0 decoded the mixed-animation fixture and verified its composited
pixels, replacement, blending, disposal, timing and loops. It also decoded the
Native AOT CLI exports of 120 TGS frames and 30 video frames.

ImageSharp 4.1.3 rejects some extended VP8 files with odd-sized VP8 chunks while
FFmpeg and libwebp decode them. The observed failure occurs when the decoder reads
the next chunk at the VP8 padding byte. The writer retains standard RIFF padding.
This affects ImageSharp-based verification, not its measured encoding calls.
