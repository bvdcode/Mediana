# Representative performance comparisons

The [format catalog](../formats/README.md) tracks variants and implementation.
This register tracks useful operations: an ordinary file and a difficult or long
file where they exercise different costs. It deliberately avoids a benchmark for
every extension, profile and parameter combination.

Each implementation has two columns: **time | memory**, for example
`123ms | 123Mb`. Values are rounded to whole milliseconds and decimal megabytes
(1,000,000 bytes); memory is peak resident memory. Empty cells mean the target
run remains open. A measured subset
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

Additional complete-video decode reports cover [VP8](../vp8.md#complete-decode-performance),
[VP9](../vp9.md#complete-decode-performance), [Theora](../theora.md#complete-decode-performance)
and [MPEG-4 Visual](results/2026-11-07-mpeg4/README.md). These are separate
source revisions and workloads from the register below.

## Workloads

<!-- comparisons:start -->

| Case | Operation / representative workload | Track | Alternative | Alternative time | Alternative memory | Mediana time | Mediana memory | Evidence |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| <a id="heic-photo-12mp"></a>`heic-photo-12mp` | decode: 12 MP real photograph to source-color RGBA8 (no ICC transform); nearest-neighbor chroma | ordinary | libheif + libde265 | 372ms | 77Mb | 610ms | 68Mb | [Measured subset](results/2026-11-06/README.md); Source-color RGBA: max error 1 byte |
| <a id="heic-photo-200mp"></a>`heic-photo-200mp` | decode: 199.76 MP real tiled photograph to source-color RGBA8 (no ICC transform); nearest-neighbor chroma | heavy | libheif + libde265 | 3997ms | 1127Mb | 5601ms | 876Mb | [Measured subset](results/2026-11-06/README.md); Source-color RGBA: max error 1 byte |
| <a id="heic-preview"></a>`heic-preview` | decode: Real ICC-profiled photograph to full-size PNG | ordinary | libheif + libde265 / heif-convert | 2507ms | 65Mb | 1509ms | 131Mb | [Measured subset](results/2026-11-06-expanded/README.md); ICC-normalized sRGB RGBA: max error 6; RMSE 0.198087; timed color policies differ |
| <a id="jpeg-photo"></a>`jpeg-photo` | decode: 2560-square baseline JPEG photograph to PNG | ordinary | libjpeg-turbo 3.1.4.1 / Pillow | 1529ms | 73Mb | 1542ms | 88Mb | [Measured subset](results/2026-11-06-expanded/README.md); RGBA8: max error 3; RMSE 0.238809; PNG sizes differ |
| <a id="jpeg-photo-progressive"></a>`jpeg-photo-progressive` | decode: 2560-square progressive JPEG photograph to PNG | ordinary | libjpeg-turbo 3.1.4.1 / Pillow | 1577ms | 73Mb | 1622ms | 128Mb | [Measured subset](results/2026-11-06-expanded/README.md); RGBA8: max error 3; RMSE 0.238809; PNG sizes differ |
| <a id="png-large"></a>`png-large` | decode: 2560-square noninterlaced RGBA photograph to PNG | heavy | FFmpeg | 1028ms | 130Mb | 1291ms | 88Mb | [Measured subset](results/2026-11-06-expanded/README.md); Every RGBA byte exact |
| <a id="webp-still"></a>`webp-still` | decode: 2560-square lossy VP8 photograph to PNG | ordinary | libwebp 1.6.0 / Pillow | 1411ms | 127Mb | 586ms | 60Mb | [Measured subset](results/2026-11-06-expanded/README.md); RGBA8: max error 1; RMSE 0.059419; PNG sizes differ |
| <a id="webp-still-lossless"></a>`webp-still-lossless` | decode: 2560-square lossless VP8L photograph with alpha to PNG | ordinary | libwebp 1.6.0 / Pillow | 1637ms | 141Mb | 648ms | 58Mb | [Measured subset](results/2026-11-06-expanded/README.md); Every RGBA byte exact; PNG sizes differ |
| <a id="webp-animation"></a>`webp-animation` | decode: All 177 composited frames of a 512-square lossless animation | heavy | libwebp 1.3.2 | 86ms | 7Mb | 203ms | 26Mb | [Measured subset](results/2026-11-06-expanded/README.md); All 177 composited RGBA frames exact |
| <a id="webp-encode"></a>`webp-encode` | encode: 2560-square photograph with alpha to lossless WebP | ordinary | libwebp 1.3.2 / cwebp | 3509ms | 212Mb | 1230ms | 146Mb | [Measured subset](results/2026-11-06-expanded/README.md); Lossless: both outputs decode exactly to source; encoded sizes differ |
| <a id="webp-encode-lossy"></a>`webp-encode-lossy` | encode: 2560-square photograph with alpha to lossy WebP; quality setting 75; source error and size measured | ordinary | libwebp 1.3.2 / cwebp | 772ms | 145Mb | 1364ms | 117Mb | [Measured subset](results/2026-11-06-expanded/README.md); Source RGBA RMSE: reference 4.753252 / Mediana 4.361002; sizes differ; quality not matched |
| <a id="raster-pages"></a>`raster-pages` | decode: Complete single-page 2560-square RGB LZW TIFF to PNG | ordinary | FFmpeg | 615ms | 121Mb | 713ms | 72Mb | [Measured subset](results/2026-11-06-expanded/README.md); Single LZW TIFF page: every RGBA byte exact |
| <a id="jp2-scan"></a>`jp2-scan` | decode: 2560-square 8-bit reversible JP2 photograph with 512-square tiles | heavy | OpenJPEG 2.5.0 | 5133ms | 90Mb | 5599ms | 350Mb | [Measured subset](results/2026-11-06-expanded/README.md); Reversible tiled JP2: every RGBA byte exact |
| <a id="jbig2-scan"></a>`jbig2-scan` | decode: Complete 2048x3072 monochrome MMR text/rule page | heavy | jbig2dec 0.20 | 12ms | 4Mb | 139ms | 48Mb | [Measured subset](results/2026-11-06-expanded/README.md); Complete MMR page: every RGBA byte exact |
| <a id="flac-track"></a>`flac-track` | decode: Complete 596-second stereo film soundtrack to PCM16 WAVE | ordinary | FFmpeg | 587ms | 54Mb | 2510ms | 28Mb | [Measured subset](results/2026-11-06-expanded/README.md); All 57262080 PCM16 samples exact |
| <a id="flac-sustained"></a>`flac-sustained` | decode: Complete 10-minute stereo stress stream to PCM16 WAVE | heavy | FFmpeg | 629ms | 54Mb | 1574ms | 28Mb | [Measured subset](results/2026-11-06/README.md); Every PCM sample exact |
| <a id="mp3-long"></a>`mp3-long` | decode: Complete 2460.096-second 48 kHz stereo MP3 stress input to PCM16 WAVE | heavy | FFmpeg | 2596ms | 55Mb | 7131ms | 28Mb | [Measured subset](results/2026-11-06/README.md); PCM16: max error 1 unit |
| <a id="aac-multichannel"></a>`aac-multichannel` | decode: Complete 596-second AAC-LC 5.1 soundtrack in M4A to PCM16 WAVE | heavy | FFmpeg | 1130ms | 58Mb | 4062ms | 29Mb | [Measured subset](results/2026-11-06-expanded/README.md); Accuracy issue: PCM16 max error 11291; RMSE 24.194438; same sample count |
| <a id="audio-resample"></a>`audio-resample` | process: Complete 2460.096-second stereo MP3; mono downmix and 48-to-16 kHz resampling | heavy | FFmpeg / libswresample | 2774ms | 55Mb | 27598ms | 32Mb | [Measured subset](results/2026-11-06-expanded/README.md); PCM16 max error 356; RMSE 0.294830; resampling filters differ |
| <a id="avc-decode-30m"></a>`avc-decode-30m` | decode: All 43201 frames of a 30-minute 720p video; native YUV decoding plus per-frame SHA-256 | heavy | FFmpeg | 214144ms | 103Mb | 411946ms | 102Mb | [Measured subset](results/2026-11-06-expanded/README.md); All 43201 YUV frame hashes exact; timing records differ; includes hashing |
| <a id="avc-transcode-30m"></a>`avc-transcode-30m` | encode: Complete 30-minute 720p24 video-only transcode; 10 Mbit/s target; source PSNR and size measured | heavy | FFmpeg / libx264 | 249075ms | 143Mb | 1340327ms | 157Mb | [Measured subset](results/2026-11-06-expanded/README.md); All 43201 frames; source YUV PSNR: reference 48.89 / Mediana 45.34 dB; quality not matched |
| <a id="remux-hls"></a>`remux-hls` | write: Complete 30-minute AVC/AAC MP4 to MPEG-TS stream copy | heavy | FFmpeg | 1208ms | 65Mb | 1812ms | 37Mb | [Measured subset](results/2026-11-06-expanded/README.md); All 43201 decoded frames and copied AAC access units exact |
| <a id="hls-segment-30m"></a>`hls-segment-30m` | write: Complete 30-minute AVC/AAC VOD segmentation; playlist and every MPEG-TS segment | heavy | FFmpeg | 1481ms | 96Mb | 3365ms | 53Mb | [Measured subset](results/2026-11-06-expanded/README.md); Complete VOD: all 43201 decoded frames and copied AAC access units exact |
| <a id="pdf-page"></a>`pdf-page` | render: Letter page with Core14 text and 540 vector rectangles at 96 DPI | ordinary | Poppler 26.05.0 | 48ms | 16Mb | 76ms | 107Mb | [Measured subset](results/2026-11-06-expanded/README.md); 816x1056; RGBA RMSE 12.936392; text/rasterization differs |
| <a id="pdf-scan"></a>`pdf-scan` | render: Letter page containing a 2560-square JPEG and grayscale soft mask at 96 DPI | heavy | Poppler 26.05.0 | 424ms | 16Mb | 906ms | 166Mb | [Measured subset](results/2026-11-06-expanded/README.md); 816x1056; RGBA RMSE 10.190034; rendering differs |
| <a id="svg-scene"></a>`svg-scene` | render: 2560-square scene; 180 gradient circles plus clipping and Gaussian blur | heavy | librsvg 2.58.0 | 1615ms | 180Mb | 31414ms | 908Mb | [Measured subset](results/2026-11-06-expanded/README.md); RGBA RMSE 0.896234; max error 76; rasterization differs |
| <a id="tgs-animation"></a>`tgs-animation` | render: All 180 frames of a 512-square 44-layer sticker; supersampling 1 | heavy | rlottie ea06d2f | 459ms | 10Mb | 6337ms | 39Mb | [Measured subset](results/2026-11-06-expanded/README.md); All 180 frames compared; RGBA RMSE 11.438445; rendering differs |
| <a id="word-page"></a>`word-page` | render: First Letter page; paragraphs/table/photo/footer using installed DejaVu Sans at 96 DPI | ordinary | LibreOfficeDev 26.8 alpha0 | 436ms | 241Mb | 1245ms | 168Mb | [Measured subset](results/2026-11-06-expanded/README.md); 816x1056; RGBA RMSE 50.849370; page layout differs |
| <a id="presentation-slide"></a>`presentation-slide` | render: 960x540 slide; inherited title layout plus photo and themed chart shapes | ordinary | LibreOfficeDev 26.8 alpha0 | 464ms | 203Mb | 1283ms | 170Mb | [Measured subset](results/2026-11-06-expanded/README.md); 960x540; RGBA RMSE 14.133231; rendering differs |
| <a id="spreadsheet-read"></a>`spreadsheet-read` | read: All 400000 numeric XLSX cells including 80000 cached formulas; LE binary64 export | heavy | ExcelDataReader 3.9.0 / .NET 10 JIT | 520ms | 50Mb | 355ms | 20Mb | [Measured subset](results/2026-11-06-expanded/README.md); All 400000 binary64 numeric values exact; JIT reference / AOT Mediana |
| <a id="mesh-preview"></a>`mesh-preview` | render: Complete 28-mesh dog.3mf model to 512-square RGBA; warmed JIT operation mean and lifetime RSS | heavy | Render.NET | 346ms | 190Mb | 383ms | 232Mb | [Measured subset](results/2026-11-06-expanded/README.md); RGBA exact; warmed JIT operation mean and lifetime RSS |
| <a id="ogg-index"></a>`ogg-index` | read: Complete 1800-second Ogg Opus scan and all encoded payload bytes | heavy | FFmpeg | 413ms | 54Mb | 172ms | 21Mb | [Measured subset](results/2026-11-06-expanded/README.md); All 14387962 encoded payload bytes exact |

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
- Report elapsed time and peak resident memory. Elapsed time includes processing
  and waiting; managed allocations and peak resident memory are different quantities.
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
