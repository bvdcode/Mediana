# Whole-file comparison: 2026-11-06

Initial four-workload snapshot of Mediana revision
[`a3c5dbf6e253c3b1e5433922cbc2dfc644ddee69`](https://github.com/bvdcode/Mediana/tree/a3c5dbf6e253c3b1e5433922cbc2dfc644ddee69).
Both managed executables use that product source snapshot. The HEIF consumer is
the [standalone public-API caller](../../../../benchmarks/Mediana.MediaComparison/HeifDecode/HeifDecodeApplication.cs);
audio uses the Native AOT CLI. The native HEIF reference is a
[small libheif caller](../../../../benchmarks/Mediana.MediaComparison/heif_reference.c).

## Conditions

- Intel Core i5-12450H; Linux x64, kernel 6.8.0-147, glibc 2.39; .NET SDK 10.0.403,
  runtime 10.0.14, Release Native AOT. SIMD follows the runtime/library defaults.
- Processes are pinned to logical CPUs 2 and 4, on different physical P cores.
  `DOTNET_PROCESSOR_COUNT=2`; FFmpeg input/output thread options are 2; libheif
  tile and codec thread options are 2. The managed decoder's existing scheduling
  remains in use. These settings match access to CPU, not actual worker counts.
- FFmpeg `6.1.1-3ubuntu5`; libheif `1.23.2` with libde265 `1.1.1`, nearest-neighbor chroma
  upsampling in both paths. libde265 is explicitly selected with strict decoding
  and source color-profile passthrough. Both image outputs are straight-alpha
  source-color RGBA8 with visible dimensions, geometry and alpha applied. No ICC transform or PNG
  compression occurs in the image measurements.
- Three fresh-process pairs per workload, alternating engine order. Cases run
  serially with warm input filesystem cache. Decoded files use the same **tmpfs**
  for both engines and one pair is removed after sample validation. This tests
  decode/output throughput; it does not establish SSD write performance.
- Elapsed covers startup, complete input loading/decoding, complete output write,
  finalization and process exit.
  Peak RSS includes runtime and all resident process memory. Hashing and sample
  validation are outside timing; numerical validation runs in another process.
- This is a shared development host without exclusive CPU/frequency control.
  Medians and ranges describe this run; small differences need a controlled run.

## Results and correctness

See the [summary table](../../README.md#workloads) for time and peak memory.
Individual runs are retained in [measurements.jsonl](measurements.jsonl),
input hashes and exact command templates in [metadata.json](metadata.json),
executable/library fingerprints in [builds.json](builds.json), and every decoded
sample comparison in [validation.jsonl](validation.jsonl).

| Case | Complete decoded output | Observation against the alternative |
| --- | --- | --- |
| HEIC 12 MP | 4000×3000, 48,000,000 RGBA bytes | Maximum byte error 1; RGBA RMSE 0.139705. Same visible dimensions; outputs are not byte-identical. |
| HEIC 199.7568 MP | 16320×12240, 799,027,200 RGBA bytes | Maximum byte error 1; RGBA RMSE 0.131672. Same visible dimensions; outputs are not byte-identical. |
| FLAC 10 minutes | 28,800,000 stereo frames at 48 kHz, 57,600,000 PCM16 samples | Every decoded sample agrees exactly. WAVE headers differ. |
| MP3 41 minutes | 118,084,608 stereo frames at 48 kHz, 236,169,216 PCM16 samples | 564,424 samples differ; maximum error 1 PCM16 unit; RMSE 0.048887 units. Frame counts agree. WAVE headers differ. |

All three repetitions produce the same output hash for each engine/workload.
For these two 8-bit camera images, matching chroma expansion gives a maximum
RGBA difference of one code value. This records the observed rounding bound; it
does not establish bitstream conformance for every HEVC profile or replace the
existing native-plane reconstruction tests. ICC conversion is outside this case.

The CLI `HEIC → PNG` path rejects the 12 MP camera file with
`ICC-profiled HEIC images require an explicit color transform.` This is separate
from successful source-color decoding above. ICC-aware preview is an open case;
the decoder measurements do not count as a passing end-to-end PNG conversion.

## Individual-run ranges

| Case | Engine | Time range | Memory range |
| --- | --- | ---: | ---: |
| heic-photo-12mp | alternative | 369ms–396ms | 77Mb–77Mb |
| heic-photo-12mp | mediana | 572ms–643ms | 68Mb–68Mb |
| heic-photo-200mp | alternative | 3420ms–4038ms | 1127Mb–1127Mb |
| heic-photo-200mp | mediana | 5595ms–5759ms | 873Mb–876Mb |
| flac-sustained | alternative | 596ms–723ms | 54Mb–54Mb |
| flac-sustained | mediana | 1570ms–1581ms | 28Mb–28Mb |
| mp3-long | alternative | 2539ms–2696ms | 55Mb–55Mb |
| mp3-long | mediana | 7072ms–7248ms | 28Mb–28Mb |

## Input identity

Exact byte counts and SHA-256 values are in [metadata.json](metadata.json).

| Local input name | Source and workload character |
| --- | --- |
| `photo-12mp.heic` | Original `20260201_164721.heic` in the [Heic.NET camera corpus](https://github.com/bvdcode/Heic.NET/blob/main/corpus/README.md): 48 tiles, real photograph. |
| `photo-200mp.heic` | Original `20260223_110514.heic` in the same corpus: 768 tiles, real photograph. |
| `stress-10m.flac` | Repeated [original synthetic harmonic signal](../../../../tests/Mediana.Tests/Data/Waveform/README.md), 600 seconds, 48 kHz stereo, PCM16 source, FLAC compression level 5. This is sustained synthetic audio, not a complete real music track. |
| `recording-long.mp3` | Existing deterministic long stress input, encoded duration 2460.096 seconds, 48 kHz stereo. Complete frame/sample validation is recorded; the original generation command is unavailable. It is not treated as a real call recording. |

The FLAC input can be reproduced using the repository's signal:

```sh
ffmpeg -nostdin -v error -n -stream_loop -1 \
  -i tests/Mediana.Tests/Data/Waveform/music.wav -t 600 -map 0:a:0 \
  -map_metadata -1 -c:a flac -compression_level 5 -sample_fmt s16 stress-10m.flac
```

The selected actual files, rather than their filenames alone, define this series.
A real full music track, a real long call, AAC multichannel audio and full
30-minute video remain separate target cases in the register.
