# Representative performance comparisons

The [format catalog](../formats/README.md) tracks variants and implementation.
This register tracks useful operations: an ordinary file and a difficult or long
file where they exercise different costs. It deliberately avoids a benchmark for
every extension, profile and parameter combination.

Each row identifies an alternative and reserves separate columns for its results
and Mediana's results. Values are **elapsed time / total CPU time / peak RSS**,
with a unit beside every number: `ms` is milliseconds, `s` is seconds
(`1 s = 1000 ms`), and `MiB` is 1,048,576 bytes. Subsecond durations use `ms`.
CPU time sums processor time across all threads: two cores working for one second
consume about two CPU seconds. It describes processor cost; elapsed time describes
how long the operation takes. Empty cells mean the target run remains open. A measured subset
does not establish every variant linked to that row. Existing narrower reports
remain related evidence; they do not close a 30-minute target run.

The register contains 32 measured workloads. The
[original four-case report](results/2026-11-06/README.md) and
[whole-operation report](results/2026-11-06-expanded/README.md) preserve commands,
versions, input hashes, individual repetitions and output comparisons. HEIC
source-color decoding and ICC-aware CLI preview have separate rows. Complete
30-minute decoding, transcoding, remuxing and HLS runs have separate rows too.
The mesh comparison explicitly uses warmed JIT operation means; other rows use
fresh-process timings. Results describe the recorded source revisions.

## Workloads

<!-- comparisons:start -->

| Case | Operation / representative workload | Track | Alternative | Alternative: elapsed / CPU (ms or s) / peak RSS (MiB) | Mediana: elapsed / CPU (ms or s) / peak RSS (MiB) | Evidence |
| --- | --- | --- | --- | --- | --- | --- |
| <a id="heic-photo-12mp"></a>`heic-photo-12mp` | decode: 12 MP real photograph to source-color RGBA8 (no ICC transform); nearest-neighbor chroma | ordinary | libheif + libde265 | 371.5 ms / CPU 588.0 ms / RSS 73.2 MiB | 610.1 ms / CPU 1.136 s / RSS 65.0 MiB | [Measured subset](results/2026-11-06/README.md); Source-color RGBA: max error 1 byte |
| <a id="heic-photo-200mp"></a>`heic-photo-200mp` | decode: 199.76 MP real tiled photograph to source-color RGBA8 (no ICC transform); nearest-neighbor chroma | heavy | libheif + libde265 | 3.997 s / CPU 6.189 s / RSS 1074.8 MiB | 5.601 s / CPU 10.930 s / RSS 835.2 MiB | [Measured subset](results/2026-11-06/README.md); Source-color RGBA: max error 1 byte |
| <a id="heic-preview"></a>`heic-preview` | decode: Real ICC-profiled photograph to full-size PNG | ordinary | libheif + libde265 / heif-convert | 2.507 s / CPU 2.724 s / RSS 62.1 MiB | 1.509 s / CPU 2.021 s / RSS 125.0 MiB | [Measured subset](results/2026-11-06-expanded/README.md); ICC-normalized sRGB RGBA: max error 6; RMSE 0.198087; timed color policies differ |
| <a id="jpeg-photo"></a>`jpeg-photo` | decode: 2560-square baseline JPEG photograph to PNG | ordinary | libjpeg-turbo 3.1.4.1 / Pillow | 1.529 s / CPU 1.527 s / RSS 69.2 MiB | 1.542 s / CPU 1.546 s / RSS 84.1 MiB | [Measured subset](results/2026-11-06-expanded/README.md); RGBA8: max error 3; RMSE 0.238809; PNG sizes differ |
| <a id="jpeg-photo-progressive"></a>`jpeg-photo-progressive` | decode: 2560-square progressive JPEG photograph to PNG | ordinary | libjpeg-turbo 3.1.4.1 / Pillow | 1.577 s / CPU 1.577 s / RSS 69.4 MiB | 1.622 s / CPU 1.620 s / RSS 121.6 MiB | [Measured subset](results/2026-11-06-expanded/README.md); RGBA8: max error 3; RMSE 0.238809; PNG sizes differ |
| <a id="png-large"></a>`png-large` | decode: 2560-square noninterlaced RGBA photograph to PNG | heavy | FFmpeg | 1.028 s / CPU 1.027 s / RSS 123.8 MiB | 1.291 s / CPU 1.298 s / RSS 83.5 MiB | [Measured subset](results/2026-11-06-expanded/README.md); Every RGBA byte exact |
| <a id="webp-still"></a>`webp-still` | decode: 2560-square lossy VP8 photograph to PNG | ordinary | libwebp 1.6.0 / Pillow | 1.411 s / CPU 1.317 s / RSS 120.9 MiB | 585.5 ms / CPU 591.5 ms / RSS 57.6 MiB | [Measured subset](results/2026-11-06-expanded/README.md); RGBA8: max error 1; RMSE 0.059419; PNG sizes differ |
| <a id="webp-still-lossless"></a>`webp-still-lossless` | decode: 2560-square lossless VP8L photograph with alpha to PNG | ordinary | libwebp 1.6.0 / Pillow | 1.637 s / CPU 1.633 s / RSS 134.9 MiB | 647.5 ms / CPU 651.4 ms / RSS 54.9 MiB | [Measured subset](results/2026-11-06-expanded/README.md); Every RGBA byte exact; PNG sizes differ |
| <a id="webp-animation"></a>`webp-animation` | decode: All 177 composited frames of a 512-square lossless animation | heavy | libwebp 1.3.2 | 86.5 ms / CPU 85.7 ms / RSS 6.6 MiB | 203.1 ms / CPU 241.3 ms / RSS 24.9 MiB | [Measured subset](results/2026-11-06-expanded/README.md); All 177 composited RGBA frames exact |
| <a id="webp-encode"></a>`webp-encode` | encode: 2560-square photograph with alpha to lossless WebP | ordinary | libwebp 1.3.2 / cwebp | 3.509 s / CPU 3.508 s / RSS 202.2 MiB | 1.230 s / CPU 1.231 s / RSS 139.6 MiB | [Measured subset](results/2026-11-06-expanded/README.md); Lossless: both outputs decode exactly to source; encoded sizes differ |
| <a id="webp-encode-lossy"></a>`webp-encode-lossy` | encode: 2560-square photograph with alpha to lossy WebP; quality setting 75; source error and size measured | ordinary | libwebp 1.3.2 / cwebp | 771.8 ms / CPU 758.1 ms / RSS 138.2 MiB | 1.364 s / CPU 1.365 s / RSS 111.2 MiB | [Measured subset](results/2026-11-06-expanded/README.md); Source RGBA RMSE: reference 4.753252 / Mediana 4.361002; sizes differ; quality not matched |
| <a id="raster-pages"></a>`raster-pages` | decode: Complete single-page 2560-square RGB LZW TIFF to PNG | ordinary | FFmpeg | 615.3 ms / CPU 614.5 ms / RSS 115.1 MiB | 712.6 ms / CPU 622.3 ms / RSS 69.1 MiB | [Measured subset](results/2026-11-06-expanded/README.md); Single LZW TIFF page: every RGBA byte exact |
| <a id="jp2-scan"></a>`jp2-scan` | decode: 2560-square 8-bit reversible JP2 photograph with 512-square tiles | heavy | OpenJPEG 2.5.0 | 5.133 s / CPU 5.133 s / RSS 85.6 MiB | 5.599 s / CPU 5.604 s / RSS 334.1 MiB | [Measured subset](results/2026-11-06-expanded/README.md); Reversible tiled JP2: every RGBA byte exact |
| <a id="jbig2-scan"></a>`jbig2-scan` | decode: Complete 2048x3072 monochrome MMR text/rule page | heavy | jbig2dec 0.20 | 12.0 ms / CPU 11.1 ms / RSS 3.8 MiB | 139.0 ms / CPU 139.5 ms / RSS 45.6 MiB | [Measured subset](results/2026-11-06-expanded/README.md); Complete MMR page: every RGBA byte exact |
| <a id="flac-track"></a>`flac-track` | decode: Complete 596-second stereo film soundtrack to PCM16 WAVE | ordinary | FFmpeg | 587.1 ms / CPU 688.7 ms / RSS 51.9 MiB | 2.510 s / CPU 2.610 s / RSS 26.8 MiB | [Measured subset](results/2026-11-06-expanded/README.md); All 57262080 PCM16 samples exact |
| <a id="flac-sustained"></a>`flac-sustained` | decode: Complete 10-minute stereo stress stream to PCM16 WAVE | heavy | FFmpeg | 628.9 ms / CPU 702.2 ms / RSS 52.0 MiB | 1.574 s / CPU 1.640 s / RSS 26.6 MiB | [Measured subset](results/2026-11-06/README.md); Every PCM sample exact |
| <a id="mp3-long"></a>`mp3-long` | decode: Complete 2460.096-second 48 kHz stereo MP3 stress input to PCM16 WAVE | heavy | FFmpeg | 2.596 s / CPU 2.954 s / RSS 52.2 MiB | 7.131 s / CPU 8.345 s / RSS 26.9 MiB | [Measured subset](results/2026-11-06/README.md); PCM16: max error 1 unit |
| <a id="aac-multichannel"></a>`aac-multichannel` | decode: Complete 596-second AAC-LC 5.1 soundtrack in M4A to PCM16 WAVE | heavy | FFmpeg | 1.130 s / CPU 1.210 s / RSS 54.9 MiB | 4.062 s / CPU 4.352 s / RSS 27.8 MiB | [Measured subset](results/2026-11-06-expanded/README.md); Accuracy issue: PCM16 max error 11291; RMSE 24.194438; same sample count |
| <a id="audio-resample"></a>`audio-resample` | process: Complete 2460.096-second stereo MP3; mono downmix and 48-to-16 kHz resampling | heavy | FFmpeg / libswresample | 2.774 s / CPU 3.102 s / RSS 52.6 MiB | 27.598 s / CPU 29.675 s / RSS 30.4 MiB | [Measured subset](results/2026-11-06-expanded/README.md); PCM16 max error 356; RMSE 0.294830; resampling filters differ |
| <a id="avc-decode-30m"></a>`avc-decode-30m` | decode: All 43201 frames of a 30-minute 720p video; native YUV decoding plus per-frame SHA-256 | heavy | FFmpeg | 214.144 s / CPU 232.315 s / RSS 98.3 MiB | 411.946 s / CPU 415.131 s / RSS 97.8 MiB | [Measured subset](results/2026-11-06-expanded/README.md); All 43201 YUV frame hashes exact; timing records differ; includes hashing |
| <a id="avc-transcode-30m"></a>`avc-transcode-30m` | encode: Complete 30-minute 720p24 video-only transcode; 10 Mbit/s target; source PSNR and size measured | heavy | FFmpeg / libx264 | 249.075 s / CPU 448.349 s / RSS 136.5 MiB | 1340.327 s / CPU 1345.048 s / RSS 149.5 MiB | [Measured subset](results/2026-11-06-expanded/README.md); All 43201 frames; source YUV PSNR: reference 48.89 / Mediana 45.34 dB; quality not matched |
| <a id="remux-hls"></a>`remux-hls` | write: Complete 30-minute AVC/AAC MP4 to MPEG-TS stream copy | heavy | FFmpeg | 1.208 s / CPU 1.740 s / RSS 61.9 MiB | 1.812 s / CPU 2.584 s / RSS 35.5 MiB | [Measured subset](results/2026-11-06-expanded/README.md); All 43201 decoded frames and copied AAC access units exact |
| <a id="hls-segment-30m"></a>`hls-segment-30m` | write: Complete 30-minute AVC/AAC VOD segmentation; playlist and every MPEG-TS segment | heavy | FFmpeg | 1.481 s / CPU 2.004 s / RSS 91.3 MiB | 3.365 s / CPU 4.446 s / RSS 50.4 MiB | [Measured subset](results/2026-11-06-expanded/README.md); Complete VOD: all 43201 decoded frames and copied AAC access units exact |
| <a id="pdf-page"></a>`pdf-page` | render: Letter page with Core14 text and 540 vector rectangles at 96 DPI | ordinary | Poppler 26.05.0 | 48.1 ms / CPU 47.8 ms / RSS 15.5 MiB | 76.4 ms / CPU 82.7 ms / RSS 102.2 MiB | [Measured subset](results/2026-11-06-expanded/README.md); 816x1056; RGBA RMSE 12.936392; text/rasterization differs |
| <a id="pdf-scan"></a>`pdf-scan` | render: Letter page containing a 2560-square JPEG and grayscale soft mask at 96 DPI | heavy | Poppler 26.05.0 | 423.7 ms / CPU 423.4 ms / RSS 15.5 MiB | 905.5 ms / CPU 913.8 ms / RSS 158.7 MiB | [Measured subset](results/2026-11-06-expanded/README.md); 816x1056; RGBA RMSE 10.190034; rendering differs |
| <a id="svg-scene"></a>`svg-scene` | render: 2560-square scene; 180 gradient circles plus clipping and Gaussian blur | heavy | librsvg 2.58.0 | 1.615 s / CPU 1.493 s / RSS 171.2 MiB | 31.414 s / CPU 31.727 s / RSS 866.4 MiB | [Measured subset](results/2026-11-06-expanded/README.md); RGBA RMSE 0.896234; max error 76; rasterization differs |
| <a id="tgs-animation"></a>`tgs-animation` | render: All 180 frames of a 512-square 44-layer sticker; supersampling 1 | heavy | rlottie ea06d2f | 458.7 ms / CPU 480.4 ms / RSS 9.2 MiB | 6.337 s / CPU 6.343 s / RSS 37.4 MiB | [Measured subset](results/2026-11-06-expanded/README.md); All 180 frames compared; RGBA RMSE 11.438445; rendering differs |
| <a id="word-page"></a>`word-page` | render: First Letter page; paragraphs/table/photo/footer using installed DejaVu Sans at 96 DPI | ordinary | LibreOfficeDev 26.8 alpha0 | 435.8 ms / CPU 352.6 ms / RSS 229.6 MiB | 1.245 s / CPU 1.242 s / RSS 160.4 MiB | [Measured subset](results/2026-11-06-expanded/README.md); 816x1056; RGBA RMSE 50.849370; page layout differs |
| <a id="presentation-slide"></a>`presentation-slide` | render: 960x540 slide; inherited title layout plus photo and themed chart shapes | ordinary | LibreOfficeDev 26.8 alpha0 | 463.8 ms / CPU 466.3 ms / RSS 193.7 MiB | 1.283 s / CPU 1.282 s / RSS 162.3 MiB | [Measured subset](results/2026-11-06-expanded/README.md); 960x540; RGBA RMSE 14.133231; rendering differs |
| <a id="spreadsheet-read"></a>`spreadsheet-read` | read: All 400000 numeric XLSX cells including 80000 cached formulas; LE binary64 export | heavy | ExcelDataReader 3.9.0 / .NET 10 JIT | 519.9 ms / CPU 524.9 ms / RSS 47.9 MiB | 355.2 ms / CPU 356.8 ms / RSS 18.6 MiB | [Measured subset](results/2026-11-06-expanded/README.md); All 400000 binary64 numeric values exact; JIT reference / AOT Mediana |
| <a id="mesh-preview"></a>`mesh-preview` | render: Complete 28-mesh dog.3mf model to 512-square RGBA; warmed JIT operation mean and lifetime RSS | heavy | Render.NET | 345.9 ms / CPU 414.0 ms / RSS 181.2 MiB | 382.9 ms / CPU 468.3 ms / RSS 221.5 MiB | [Measured subset](results/2026-11-06-expanded/README.md); RGBA exact; warmed JIT operation mean and lifetime RSS |
| <a id="ogg-index"></a>`ogg-index` | read: Complete 1800-second Ogg Opus scan and all encoded payload bytes | heavy | FFmpeg | 413.2 ms / CPU 610.9 ms / RSS 51.2 MiB | 172.4 ms / CPU 238.5 ms / RSS 19.9 MiB | [Measured subset](results/2026-11-06-expanded/README.md); All 14387962 encoded payload bytes exact |

<!-- comparisons:end -->

## HEIC direction and chroma reconstruction

A HEIC workload identifies the operation direction and the chroma reconstruction
method separately. The decoder exposes [four reconstruction kernels](../chroma-reconstruction.md).
Bilinear is the current default. The original paired measurements explicitly use
nearest neighbor; their timings do not establish the cost of another kernel.

| Direction / stage | Method | Reconstruction / trade-off | Mediana status | Reference / measurements |
| --- | --- | --- | --- | --- |
| Decode: chroma expansion | Nearest neighbor | One sample; least filtering work, possible stepped color edges | Implemented | libheif/libde265; [paired 12 MP](#heic-photo-12mp) and [200 MP](#heic-photo-200mp); [current four-mode runs](../chroma-reconstruction.md#whole-file-measurements) |
| Decode: chroma expansion | Bilinear | Two taps per subsampled axis; smooth edges, possible color-detail blur | Implemented; default | [Complete 12 MP and 200 MP runs](../chroma-reconstruction.md#whole-file-measurements); native sample-location/edge behavior differs |
| Decode: chroma expansion | Bicubic (Catmull-Rom) | Four taps per subsampled axis; broader reconstruction, possible overshoot | Implemented | [Complete 12 MP and 200 MP runs](../chroma-reconstruction.md#whole-file-measurements); independent component-plane checks |
| Decode: chroma expansion | Lanczos3 | Six taps per subsampled axis; windowed sinc, possible ringing | Implemented | [Complete 12 MP and 200 MP runs](../chroma-reconstruction.md#whole-file-measurements); radius-three window and independent component-plane checks |
| Encode: HEVC image and HEIF writing | Separate encoder operation | Subsampling, compression and output quality need separate checks | Not implemented | Decoder results do not establish encoding support |

Nearest neighbor repeats a color sample; bilinear mixes neighboring samples;
bicubic uses a wider cubic neighborhood; Lanczos uses a windowed sinc filter.
Compare speed, memory and color-edge quality with the same chroma siting, range,
precision and color transform. The method name alone does not establish identical
filter coefficients or boundary handling. Encoding needs separate chroma
subsampling, compression quality, file-size and timing checks.

## Measurement contract

- Compare the same input, selected streams/pages, dimensions, precision, color
  interpretation and output operation. A raw decoder result and a PNG preview
  are separate workloads. For documents fix fonts, resolution and page selection.
- Report elapsed time, process user + system CPU time and peak resident memory.
  CPU time sums processor work; elapsed time includes processing and waiting. CPU percentages,
  managed allocations and process RSS are different quantities.
- Record source revision, build/runtime, reference versions, input hash, options,
  CPU topology/affinity, thread settings, output storage, repetitions and cache
  conditions. Match CPU access explicitly. The same CPU allowance does not imply
  the implementations use identical internal parallelism.
- Use at least three alternating fresh-process pairs for an initial comparison.
  Preserve individual values and report medians plus ranges. Run without other
  heavy jobs for acceptance of small performance changes. Shared-host snapshots
  describe that run, not a universal speed ratio.
- Process the whole file, every frame/sample and finalization where the case
  requires it. Short previews do not represent sustained video transcoding.
  Decode, encode, rendering, remuxing and indexing have separate operations.
- Validate all output samples outside timing. Compare lossless samples exactly;
  disclose lossy rounding and rendering differences. For encoding, record output
  size/bitrate and decoded quality alongside time. A faster low-quality encoder
  does not establish a matched-quality advantage.
- Measure CPU software paths first. Default-thread runs, fixed CPU budgets,
  hardware acceleration, startup latency and reused decoder instances are
  separate comparison series.

HEIF is the image container family; HEIC normally carries HEVC-coded images.
The first HEIC reference here is **libheif + libde265**. AVIF uses HEIF structure
with AV1 and needs its own decoder/measurement. None of these alternatives imply
a runtime dependency for Mediana.

The register is [cases.csv](cases.csv); measured medians are
[results.csv](results.csv). Update their evidence, then run
`python3 tools/format-catalog.py`; `--check` verifies both generated tables.
The [comparison runner](../../benchmarks/Mediana.MediaComparison/README.md)
preserves individual process and sample-validation records. A row enters the
summary only after its input, full operation and output observations are recorded.
