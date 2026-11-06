# Representative performance comparisons

The [format catalog](../formats/README.md) tracks variants and implementation.
This register tracks useful operations: an ordinary file and a difficult or long
file where they exercise different costs. It deliberately avoids a benchmark for
every extension, profile and parameter combination.

Each row identifies an alternative and reserves separate columns for its results
and Mediana's results. Values are **elapsed seconds / total CPU seconds / peak
resident MiB**. Empty cells mean the target run remains open. A measured subset
does not establish every variant linked to that row. Existing narrower reports
remain related evidence; they do not close a 30-minute target run.

The current four measured workloads and their complete commands, versions,
input hashes, repetitions and correctness observations are in the
[2026-11-06 report](results/2026-11-06/README.md). HEIC source-color decoding is measured separately from ICC-aware CLI preview.
The preview remains open in the measured revision; decoder timings do not close
that conversion case.

## Workloads

<!-- comparisons:start -->

| Case | Operation / representative workload | Track | Alternative | Alternative: elapsed / CPU / peak RAM | Mediana: elapsed / CPU / peak RAM | Evidence |
| --- | --- | --- | --- | --- | --- | --- |
| <a id="heic-photo-12mp"></a>`heic-photo-12mp` | decode: 12 MP real photograph to source-color RGBA8 (no ICC transform) | ordinary | libheif + libde265 | 0.372 s / 0.588 s / 73.2 MiB | 0.610 s / 1.136 s / 65.0 MiB | [Measured subset](results/2026-11-06/README.md) |
| <a id="heic-photo-200mp"></a>`heic-photo-200mp` | decode: 199.76 MP real tiled photograph to source-color RGBA8 (no ICC transform) | heavy | libheif + libde265 | 3.997 s / 6.189 s / 1074.8 MiB | 5.601 s / 10.930 s / 835.2 MiB | [Measured subset](results/2026-11-06/README.md) |
| <a id="heic-preview"></a>`heic-preview` | decode: Real ICC-profiled photograph to full-size PNG | ordinary | libheif + libde265 / heif-convert | — | — | [Related measurements](results/2026-11-06/README.md); target run open |
| <a id="jpeg-photo"></a>`jpeg-photo` | decode: High-detail photograph and progressive scan to PNG | ordinary | FFmpeg / libjpeg-turbo | — | — | Target run open |
| <a id="png-large"></a>`png-large` | decode: Large RGBA image with alpha and interlace to PNG | heavy | FFmpeg | — | — | Target run open |
| <a id="webp-still"></a>`webp-still` | decode: Photograph and alpha image to full-size PNG | ordinary | FFmpeg / libwebp | — | — | Target run open |
| <a id="webp-animation"></a>`webp-animation` | decode: Composite every frame including disposal and blending | heavy | libwebp | — | — | Target run open |
| <a id="webp-encode"></a>`webp-encode` | encode: Photograph and difficult alpha/texture images | ordinary | ImageSharp / libwebp | — | — | [Related measurements](../../benchmarks/Mediana.WebpBenchmarks/README.md); target run open |
| <a id="raster-pages"></a>`raster-pages` | decode: Representative bitmap and compressed multipage scan | ordinary | FFmpeg / libtiff | — | — | Target run open |
| <a id="jp2-scan"></a>`jp2-scan` | decode: Large tiled high-precision scanned image | heavy | OpenJPEG | — | — | Target run open |
| <a id="jbig2-scan"></a>`jbig2-scan` | decode: Dense monochrome text/image page with symbol dictionaries | heavy | jbig2dec | — | — | Target run open |
| <a id="flac-track"></a>`flac-track` | decode: Complete music track to PCM16 WAVE | ordinary | FFmpeg / libFLAC | — | — | Target run open |
| <a id="flac-sustained"></a>`flac-sustained` | decode: Complete 10-minute stereo stress stream to PCM16 WAVE | heavy | FFmpeg | 0.629 s / 0.702 s / 52.0 MiB | 1.574 s / 1.640 s / 26.6 MiB | [Measured subset](results/2026-11-06/README.md) |
| <a id="mp3-long"></a>`mp3-long` | decode: Complete long mono/stereo recording to PCM16 WAVE | heavy | FFmpeg | 2.596 s / 2.954 s / 52.2 MiB | 7.131 s / 8.345 s / 26.9 MiB | [Measured subset](results/2026-11-06/README.md) |
| <a id="aac-multichannel"></a>`aac-multichannel` | decode: Complete long 5.1 AAC stream to PCM16 WAVE | heavy | FFmpeg | — | — | Target run open |
| <a id="audio-resample"></a>`audio-resample` | decode: Complete long recording downmix and 48-to-16 kHz resampling | heavy | FFmpeg / libswresample | — | — | Target run open |
| <a id="avc-decode-30m"></a>`avc-decode-30m` | decode: Every frame of a 30-minute HD video to native YUV | heavy | FFmpeg | — | — | Target run open |
| <a id="avc-transcode-30m"></a>`avc-transcode-30m` | encode: Complete 30-minute HD transcode at matched quality/rate | heavy | FFmpeg / libx264 | — | — | [Related measurements](../h264-hd-encoding.md); target run open |
| <a id="remux-hls"></a>`remux-hls` | write: Complete 30-minute A/V stream copy and VOD segmentation | heavy | FFmpeg | — | — | Target run open |
| <a id="pdf-page"></a>`pdf-page` | render: First-page preview of vector/text/image document at fixed DPI | ordinary | Poppler | — | — | Target run open |
| <a id="pdf-scan"></a>`pdf-scan` | render: Large scanned page with masks and embedded image codecs | heavy | Poppler | — | — | Target run open |
| <a id="svg-scene"></a>`svg-scene` | render: Text/clipping/gradient/filter artwork at 2560 square | heavy | librsvg / resvg | — | — | Target run open |
| <a id="tgs-animation"></a>`tgs-animation` | render: Complete representative sticker animation | heavy | rlottie / lottie-web | — | — | Target run open |
| <a id="word-page"></a>`word-page` | render: Paginated text/tables/images with fixed embedded fonts | ordinary | LibreOffice | — | — | Target run open |
| <a id="presentation-slide"></a>`presentation-slide` | render: Slide with inherited layout and picture fills at fixed size | ordinary | LibreOffice | — | — | Target run open |
| <a id="spreadsheet-read"></a>`spreadsheet-read` | read: Large workbook streaming all cell values and cached formulas | heavy | ExcelDataReader | — | — | Target run open |
| <a id="mesh-preview"></a>`mesh-preview` | render: Complex mesh and component assembly to 512-square RGBA | heavy | Render.NET | — | — | [Related measurements](../mesh-performance.md); target run open |
| <a id="ogg-index"></a>`ogg-index` | read: Complete long Opus/Vorbis indexing and packet extraction | heavy | FFmpeg / libogg | — | — | Target run open |

<!-- comparisons:end -->

## Measurement contract

- Compare the same input, selected streams/pages, dimensions, precision, color
  interpretation and output operation. A raw decoder result and a PNG preview
  are separate workloads. For documents fix fonts, resolution and page selection.
- Report elapsed time, process user + system CPU time and peak resident memory.
  CPU seconds measure work; elapsed seconds measure waiting. CPU percentages,
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
