# H.264 encoding comparison at matched size

Each input contains 150 YUV420 frames at 30 fps. The matrix covers camera video, a translated photograph, seeded temporal noise, moving text with a grid, generated animation and a scene cut. Camera-derived 1080p inputs scale a 720p source; text and animation are generated at each target resolution. This does not substitute for native 1080p camera material.

Mediana uses .NET 11 Native AOT, CABAC, RateDistortion analysis, enabled deblocking, pooled media buffers, QP26 as the rate controller starting point, a two-second buffer and GOP60. FFmpeg 6.1.1 uses libx264 veryfast with one thread, its default B pictures, a two-second buffer and GOP60. Both read the same complete Y4M and write MPEG-TS. Reference rates are 4 Mbit/s at 720p and 9 Mbit/s at 1080p. Mediana rates are adjusted until actual elementary video payloads differ by less than 1%; QP values are not treated as equivalent quality.

Independent FFmpeg decoding measures PSNR and SSIM against the original source. Packet sizes exclude MPEG-TS overhead. All inputs reach EOF, all 150 pictures are emitted and Mediana releases its media buffers.

## Quality

| Input | x264 / Mediana payload bytes | x264 / Mediana PSNR, dB | x264 / Mediana SSIM |
| --- | ---: | ---: | ---: |
| camera-720 | 2,563,313 / 2,569,120 | 51.417 / 51.068 | 0.993616 / 0.993060 |
| photo-pan-720 | 1,680,101 / 1,675,886 | 56.961 / 52.885 | 0.997875 / 0.994711 |
| noise-720 | 2,648,306 / 2,640,958 | 33.683 / 33.722 | 0.699507 / 0.704141 |
| text-720 | 785,325 / 788,972 | 71.386 / 58.935 | 0.999997 / 0.999886 |
| animation-720 | 2,725,145 / 2,703,690 | 47.029 / 45.764 | 0.997721 / 0.995673 |
| cuts-720 | 2,900,762 / 2,894,653 | 50.228 / 48.459 | 0.996264 / 0.995174 |
| camera-1080 | 5,763,008 / 5,741,810 | 52.394 / 51.987 | 0.994745 / 0.994216 |
| photo-pan-1080 | 4,849,136 / 4,842,553 | 57.177 / 53.633 | 0.997964 / 0.995513 |
| noise-1080 | 6,371,633 / 6,371,033 | 33.022 / 33.723 | 0.692651 / 0.702244 |
| text-1080 | 1,148,225 / 1,145,719 | 74.423 / 59.616 | 0.999998 / 0.999911 |
| animation-1080 | 6,144,548 / 6,155,042 | 48.350 / 46.988 | 0.998220 / 0.996228 |
| cuts-1080 | 6,343,153 / 6,322,452 | 50.887 / 49.196 | 0.996819 / 0.995782 |

Camera results are within 0.41 dB of x264 in this matrix. The noise cases have comparable or higher aggregate PSNR/SSIM. Photograph motion, text, animation and scene cuts retain quality gaps at matched payload. The text PSNR gap is large while both SSIM values are close to 1; neither metric alone establishes perceptual equivalence.

## Initial operation measurements

Linux x64, Intel Core i5-12450H, affinity to logical CPU 11 (an efficiency core), nice -20, DOTNET_PROCESSOR_COUNT=1. Every measurement uses a fresh process. These are individual baseline runs, not repeated medians or an isolated-host speed claim. The efficiency-core measurements do not establish performance against the 720p30 target on a performance core. CPU and wall time include the complete encode and transport output; peak RSS covers the process.

| Input | x264 / Mediana CPU, s | x264 / Mediana wall, s | x264 / Mediana peak RSS, MiB |
| --- | ---: | ---: | ---: |
| camera-720 | 4.929 / 8.693 | 4.938 / 8.770 | 122.7 / 23.8 |
| photo-pan-720 | 2.075 / 3.994 | 2.079 / 3.996 | 122.2 / 23.5 |
| noise-720 | 3.824 / 6.393 | 3.838 / 6.399 | 122.2 / 23.6 |
| text-720 | 1.347 / 2.266 | 1.357 / 2.267 | 122.2 / 23.1 |
| animation-720 | 1.942 / 3.566 | 1.944 / 3.694 | 122.5 / 23.5 |
| cuts-720 | 3.586 / 5.707 | 3.588 / 5.708 | 121.8 / 23.6 |
| camera-1080 | 11.307 / 17.370 | 11.311 / 17.686 | 204.0 / 36.9 |
| photo-pan-1080 | 4.704 / 10.086 | 4.707 / 11.529 | 203.5 / 37.1 |
| noise-1080 | 7.911 / 14.852 | 7.932 / 14.854 | 205.7 / 38.2 |
| text-1080 | 2.740 / 4.184 | 2.742 / 4.185 | 202.0 / 36.2 |
| animation-1080 | 4.120 / 7.781 | 4.351 / 7.785 | 202.9 / 36.9 |
| cuts-1080 | 9.033 / 15.252 | 9.125 / 15.747 | 205.0 / 36.8 |

Mediana uses less process memory in every case. The measured CPU ratio ranges from 1.53 to 2.14. Closing the remaining gap requires evaluating prediction decisions, rate allocation and arithmetic cost together; this matrix is the baseline for subsequent changes.

A separate sampled native profile of the complete camera 720p operation attributes 7.0% of exclusive samples to chroma comparison, 4.8% to paired forward transform/quantization and 3.1% to residual reconstruction/error measurement. CABAC output and candidate-rate estimation are additional costs. These sampled percentages identify work to inspect; they are not an additive prediction of achievable acceleration.

See [macroblock analysis](h264-macroblock-analysis.md), [pipeline profiling](video-pipeline-profile.md) and [.NET 11 measurements](dotnet11.md).

## Reproduction

Use the [operation benchmark](../benchmarks/Mediana.MediaComparison/README.md):

```text
Mediana.OperationComparison h264-encode input.y4m output.ts 26 <matched-bit-rate> 60 cabac rd filter pooled
```

Reference settings:

```text
ffmpeg -threads 1 -i input.y4m -an -c:v libx264 -threads 1 -preset veryfast -b:v <reference-bit-rate> -maxrate <reference-bit-rate> -bufsize <twice-reference-bit-rate> -g 60 -keyint_min 60 -sc_threshold 0 -pix_fmt yuv420p -f mpegts reference.ts
```

Both outputs are decoded by FFmpeg and compared with the same input after resetting presentation timestamps to zero. Sum video packet sizes for the size target, rather than comparing transport file sizes. Adjust the Mediana target rate independently; retain the complete output and quality result of the closest size match. Time fresh processes separately from generation and quality analysis.

The synthetic sources use `testsrc2` for animation, temporal uniform noise with strength 18 and seed 8127, and DejaVu Sans text over a gray grid. The photograph case translates source frame 60 with a fixed 1.05 zoom. The cut case concatenates 75 camera frames and 75 animation frames. Each source is converted once to its final YUV420 resolution before either encoder reads it. A production-quality conclusion additionally requires more independently captured 1080p scenes and longer clips.

## Paired quantization experiment

The sampled transform cost motivated a fixed 52-entry table containing prepared SIMD scale, rounding and shift vectors. The table needs 3,328 bytes of data. .NET 11 `Vector128.ZipLower` constructs the alternating scale vectors. This removes repeated QP division and vector assembly from each block pair. The native function size falls from 1,499 to 1,373 bytes.

All 837 H.264 tests pass, including the independent matrix oracle for every QP; the 13 forward-transform tests also pass with intrinsics disabled. In 36 complete Native AOT encodes, all transport bytes match the original results exactly. Three fresh processes per version and case run in ABBAAB order on the same CPU 11.

| Input | Original / cached median CPU, s | Change |
| --- | ---: | ---: |
| camera-720 | 7.565 / 6.898 | -8.82% |
| noise-720 | 6.218 / 6.164 | -0.87% |
| text-720 | 2.057 / 2.012 | -2.19% |
| photo-pan-1080 | 9.986 / 10.348 | +3.62% |
| animation-1080 | 7.588 / 7.642 | +0.71% |
| cuts-1080 | 13.358 / 14.841 | +11.11% |

The change is not adopted: it improves some cases and regresses others, including scene changes. Host variation remains present, but the measurements do not establish a broadly useful gain. The original implementation remains in use. Smaller machine code and fewer arithmetic instructions alone are insufficient evidence of complete-encoding acceleration.
