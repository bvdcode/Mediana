# Complete MPEG-4 Visual decoding

The complete operation opens indexed AVI, reconstructs every native YUV420
picture, hashes every visible row with SHA-256, writes frame records and disposes
codec/container state. These measurements compare commits `69b8b31` and
`41308e7` with the same comparison driver from `208a2e4`.

## Conditions

- Intel Core i5-12450H; both processes pinned to logical CPU 2.
- .NET SDK 10.0.403/runtime 10.0.14, Release JIT, one reported processor,
  tiered compilation disabled; FFmpeg 6.1.1-3ubuntu5 uses one decoding thread.
- Three alternating fresh-process before/after pairs per input, followed by
  one FFmpeg run per pair. The operating-system file cache was not cleared.
- CPU is the Mediana operation process delta and FFmpeg child user plus system
  CPU. RSS is the process peak; managed allocations are cumulative, not live heap.
- Mediana CPU excludes host startup before the operation; FFmpeg CPU includes
  executable startup. Elapsed times use the same respective boundaries.
- No other comparison or build ran alongside these trials. Shared-host scheduling
  still affects elapsed time. Hashes, individual records and source generation
  parameters are in [measurements.json](measurements.json).

## Measurements

Values are medians, with minimum–maximum in parentheses. RSS and managed
allocations use MiB (1,048,576 bytes). The minute contains 1,440 separately
generated pictures, rather than repeated copies of the ten-second input.

| Input | Build | CPU seconds | Elapsed seconds | Peak RSS MiB | Managed allocation MiB |
| --- | --- | ---: | ---: | ---: | ---: |
| 720p-10s | Before | 1.149 (1.126–1.314) | 1.149 (1.126–1.314) | 58.758 (58.633–60.242) | 9.844 (9.841–11.939) |
| 720p-10s | After | 1.044 (1.031–1.106) | 1.044 (1.031–1.106) | 58.773 (58.719–60.672) | 9.845 (9.844–11.940) |
| 720p-10s | FFmpeg | 1.118 (1.082–1.170) | 1.122 (1.083–1.204) | 71.324 (71.312–71.730) | — |
| 720p-60s | Before | 5.621 (5.589–5.658) | 6.291 (5.658–6.620) | 65.906 (65.785–65.941) | 19.559 (19.548–19.568) |
| 720p-60s | After | 5.118 (4.979–5.153) | 5.293 (5.153–5.432) | 66.074 (65.824–66.129) | 19.557 (19.548–19.557) |
| 720p-60s | FFmpeg | 6.525 (6.367–6.868) | 6.526 (6.368–6.869) | 71.145 (71.145–71.516) | — |
| noise | Before | 1.079 (1.055–1.203) | 1.079 (1.055–1.203) | 54.031 (53.695–54.527) | 3.090 (3.090–3.841) |
| noise | After | 0.915 (0.883–1.077) | 0.915 (0.883–1.077) | 54.406 (54.184–54.418) | 3.340 (3.090–3.341) |
| noise | FFmpeg | 0.280 (0.277–0.306) | 0.281 (0.278–0.306) | 57.090 (56.715–57.215) | — |

CPU decreased about 9% on both HD inputs and 15% on noise. The complete HD
decode-and-hash operation is close to or faster than this FFmpeg configuration;
the noisy source still uses about **3.27 times** its CPU. This does not establish
codec-wide parity, encoding speed or performance on other profiles and hardware.

The sampled costs were half-pixel prediction, dequantization, inverse transforms
and coefficient decoding. Interior prediction now uses row copies or SIMD with
the same rounding. The entropy path carries its nonzero coefficient mask through
dequantization and inverse transforms, avoiding repeated scans and empty work.
No additional image-sized storage was introduced.

## Reconstruction

All nine before/after output pairs are byte-identical. Independent comparison
reads the entire displayed sequence, every visible Y/U/V sample and matching EOF.

| Input | Pictures | PSNR vs FFmpeg | Maximum sample difference |
| --- | ---: | ---: | ---: |
| 720p-10s | 240 | 72.148 dB | 3 |
| 720p-60s | 1440 | 72.078 dB | 3 |
| noise | 96 | 63.348 dB | 13 |

The noisy sequence was also decoded independently with Xvid 1.3.7: all 96
pictures match within three sample levels, with 62.996 dB PSNR. Its reference
padding uses macroblock boundaries under ISO/IEC 14496-2 clause 7.6.4. Different
integer IDCT implementations permit small numerical differences; one decoder’s
pixels alone do not define universal bit-exact conformance. The static
[partial-macroblock corpus](../../../../tests/Mediana.Codecs.Mpeg4Visual.Tests/Data/README.md)
checks 37 additional pictures against independent Xvid reconstruction.

## Reproduction

Build the comparison driver in Release, then run:

```sh
DOTNET_PROCESSOR_COUNT=1 DOTNET_TieredCompilation=0 taskset -c 2 dotnet benchmarks/Mediana.MediaComparison/Operations/bin/Release/net10.0/Mediana.OperationComparison.dll video-pooled input.avi mediana.framehash
taskset -c 2 ffmpeg -v error -threads 1 -i input.avi -an -c:v rawvideo -threads 1 -vsync 0 -f framehash -hash sha256 reference.framehash
```

Use separate output paths and processes for each repetition. Compare decoded
native samples independently of the timed operation; lossy IDCT rounding means
Mediana/FFmpeg frame hashes need not be identical. Source inputs are generated
with `testsrc2`, one native MPEG-4 encoding thread, `-q:v 4 -g 48 -bf 2 -flags +mv4`.
The noise input adds `noise=alls=50:allf=t+u`. All source hashes are recorded.
