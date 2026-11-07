# Whole-operation format comparisons: 2026-11-06

This series measures representative implemented operations, including complete
30-minute video and long audio. The [summary table](../../README.md#workloads)
separates reading, decoding, rendering, encoding and stream copy. A measured file
establishes that operation on that input; format-wide conformance remains in the
[format catalog](../../../formats/README.md).

The report adds 28 measured workloads to the original four, for 32 summary rows.
Its records contain 168 process observations and 84 paired output comparisons;
the mesh observations retain their distinct warmed-JIT scope described below.

## Source and execution

The Native AOT CLI and operation consumer use product revision
[`8ce822571594336e78f850578be875079e2490f0`](https://github.com/bvdcode/Mediana/tree/8ce822571594336e78f850578be875079e2490f0).
The standalone [operation consumer](../../../../benchmarks/Mediana.MediaComparison/Operations)
uses public APIs for whole-stream YUV decoding, animation, XLSX and Ogg extraction.
The XLSX caller explicitly allows 2,000,000 XML elements for its 400,000-cell input;
this does not change the library's default resource limits. Its reference registers
.NET code-page support before opening ExcelDataReader.

Conditions are Intel Core i5-12450H, Linux x64 kernel 6.8.0-147, glibc 2.39,
SDK 10.0.403 and runtime 10.0.14. Processes run serially on logical CPUs 2 and 4,
two different P cores, with `DOTNET_PROCESSOR_COUNT=2` and tiered compilation off.
FFmpeg receives two decoder/encoder threads; its quality validation also limits
filter threads. Matching CPU access does not establish identical internal worker
counts. No GPU or hardware codec path is selected.

Each fresh-process workload has three alternating pairs. The shared host has no
exclusive frequency or CPU control; ranges describe observed variation. Elapsed
includes startup, all requested input processing, output writing, finalization and
process exit. RSS is an individual process resident high-water mark, including
native/runtime memory. It does not sum simultaneous child-process memory.
The photograph series and small-process comparisons capture child RSS through
GNU time 1.9, excluding Python memory inherited before executable startup.
Earlier series use `wait4` RSS directly;
their selected process peaks exceed its observed launch-memory floor of about
20 MiB. The metadata identifies the resource timer where used.
Both engines write decoded
outputs to **tmpfs**. These numbers do not measure SSD throughput. The input cache
is warm; hashing and quality comparisons are outside timing.

The mesh row is explicitly different: it imports the existing
[three-process JIT study](../../../mesh-performance.md), selecting the complete
28-mesh `dog.3mf` model. Each process warms up and then measures three operations;
elapsed is the mean per operation, while RSS is the lifetime peak.
It is not a fresh single-command Native AOT timing. All six models in that source
study have matching RGBA bytes. This selected file does not exercise Production
Extension cross-part components or establish material/texture interpretation.

## Reference implementations

| Operation | Reference version and scope |
| --- | --- |
| PNG, TIFF, audio, AVC, MPEG-TS, HLS, Ogg | FFmpeg 6.1.1-3ubuntu5; libx264 for video encoding |
| JPEG baseline/progressive to PNG | libjpeg-turbo 3.1.4.1 through Pillow 12.3.0; includes Python startup and PNG export |
| Still WebP to PNG | libwebp 1.6.0 through Pillow 12.3.0; includes Python startup and PNG export |
| WebP encoding and animation decoding | libwebp 1.3.2; `cwebp` or a complete-canvas animation caller |
| HEIC to PNG | libheif 1.23.2 + libde265 1.1.1, `heif-convert`; nearest-neighbor chroma, PNG compression level 6 |
| Tiled reversible JP2 | OpenJPEG 2.5.0, `opj_decompress` |
| JBIG2 MMR page | jbig2dec 0.20 |
| SVG | librsvg 2.58.0, `rsvg-convert` |
| PDF | Poppler 26.05.0, `pdftoppm`, first page at 96 DPI |
| TGS | [Samsung rlottie](https://github.com/Samsung/rlottie/tree/ea06d2f29ba01b8d06c00a838d107f5e484ae59b), revision `ea06d2f29ba01b8d06c00a838d107f5e484ae59b`, Release build |
| DOCX/PPTX | LibreOfficeDev 26.8.0.0.alpha0, direct `soffice.bin`, isolated new profile per invocation; this is a development build |
| XLSX | ExcelDataReader 3.9.0, Release .NET 10 JIT, complete numeric-value export |
| Mesh | Render.NET source at `8d2a4375c719c9bc394f755a4c989ed685bd36b6`, Release .NET 10 JIT |

Reference tools are benchmark dependencies only. Image comparisons include complete
PNG writing, so their timing and encoded sizes describe that whole conversion.
They do not isolate entropy decoding or make different compression levels equal.
The CLI uses supersampling 2 on each axis for SVG, PDF and Office rendering;
the separate whole-animation TGS caller explicitly uses supersampling 1.
Reference renderers retain their own antialiasing algorithms. These are complete
rendering paths with the output differences recorded below.
The JPEG and VP8 references use native chroma reconstruction close to Mediana's;
an unrelated nearest/bilinear conversion difference must not be counted as a
JPEG decoder accuracy failure.

The HEIC preview includes color management. Mediana exports sRGB pixels;
`heif-convert` preserves the source ICC profile. Validation converts declared
profiles to sRGB before comparing RGBA. Timed color policies therefore differ;
this is an end-to-end preview comparison, not an isolated decoder speed ratio.
The older [source-color HEIC measurements](../2026-11-06/README.md) remain separate.
They and this pinned preview use nearest-neighbor chroma. The
[current four-mode measurements](../../../chroma-reconstruction.md#whole-file-measurements)
cover nearest, bilinear, bicubic and Lanczos on complete 12 MP and 200 MP images
at a different product revision. They do not change this pinned preview's filter.

Writer PNG export in this LibreOffice build adds one pixel to each requested
dimension. The explicit filter settings are 815 × 1055 to obtain the required
816 × 1056 output. Neither output is resized during validation. The PPTX filter
produces its requested 960 × 540 dimensions directly. The graphic export properties
are documented in the [LibreOffice manual](https://help.libreoffice.org/latest/en-US/text/shared/guide/graphic_export_params.html?DbPAR=SHARED).

## Inputs and preparation

Input byte counts and SHA-256 values are in [metadata.json](metadata.json).
No measured row substitutes a short extraction for a complete-stream operation.

- `photo-baseline.jpg` and `photo-progressive.jpg` are 2560 × 2560 photographs,
  JPEG quality 92 and 4:2:0, exported from the same RGB source. The same photograph
  supplies PNG alpha, TIFF LZW, reversible tiled JP2 and still WebP inputs.
  The source is the 12 MP camera HEIC: `heif-convert` exports its primary image,
  Pillow converts its declared ICC profile to sRGB, crops the center 3000-square
  region and resizes it to 2560 square with Lanczos. An intermediate JPEG uses
  quality 95, 4:4:4. Source hashes and geometry are in
  [preparation.json](preparation.json); image resizing is separate from HEIC chroma
  reconstruction and is outside timing.
- `large-alpha.png` adds a vertical alpha gradient to all 6,553,600 pixels.
  The TIFF is one RGB LZW page, the PNG is noninterlaced, and JP2 is reversible
  8-bit RGB with 512-square tiles. These cases do not establish other TIFF codecs,
  Adam7, high-precision JP2 or irreversible JP2 performance.
- `scan-mmr.jb2` is an authored 2048 × 3072 dense monochrome text/rule page. Its
  MMR strips are independently encoded with TIFF Group 4 and packaged as JBIG2
  generic regions. This does not measure symbol-dictionary coding.
- `vector-scene.svg` has 180 gradient circles, clipping, strokes and Gaussian blur
  on a 2560-square canvas. It contains no font substitution or text workload.
- `vector-page.pdf` is a Letter page with Core14 Helvetica text and 540 vector
  rectangles. `scan-page.pdf` embeds the full-size baseline JPEG and a grayscale
  soft mask. Both are rendered at 816 × 1056, 96 DPI.
- `report-page.docx` contains paragraphs, a table, photograph and footer on Letter
  paper. `media-slide.pptx` has inherited title layout, a photograph and twelve
  theme-colored chart shapes at 960 × 540. Both request installed DejaVu Sans;
  fonts are not embedded. Only the first page/slide is measured.
- `numeric-workbook.xlsx` has 20,000 rows × 20 numeric cells, including 80,000
  formulas with stored numeric results. Both readers write every value as
  little-endian binary64. Formula recalculation, strings and styled page rendering
  are outside this case.
- `sticker.tgs` is the tracked 512-square, 44-layer
  [trimmed/translucent-groups fixture](../../../../tests/Mediana.Tests/Data/Tgs/README.md).
  Both render all 180 source frames at supersampling 1 and write straight RGBA8.
  The reference converts premultiplied ARGB before writing. Its explicit frame
  range is 0–179: rlottie's reported total includes an extra endpoint.
- `sticker.webp` is a complete lossless animation encoded from 180 straight RGBA
  frames prepared with Ubuntu rlottie 0.1+dfsg-4ubuntu1.1. The preparation sequence
  hash identifies those pixels independently of the newer TGS timing reference.
  It has 177 stored frames after identical-frame coalescing;
  both animation decoders emit all 177 composited frames at 512 × 512.
- `soundtrack.flac` and `soundtrack.m4a` contain the complete 596-second
  [Big Buck Bunny source](../../../real-hd.md): stereo PCM16 FLAC and copied
  48 kHz 5.1 AAC respectively. `video-30m.mp4` repeats that source without
  re-encoding to about 1800 seconds, 1280 × 720, 24 fps, 43,201 video frames plus
  AAC. It is a repeated real film excerpt, not a unique 30-minute recording.
- `soundtrack-30m.opus` repeats the soundtrack for 1800 seconds and measures complete
  Ogg packet extraction. Opus decoding and chained Vorbis are outside that row.
- `long-session.mp3` is the same 2460.096-second stereo stress input as the original
  MP3 report; its generation command is unavailable. Resampling uses that entire
  file, a 0.5/0.5 mono mix and 48-to-16 kHz conversion.
- `photo-12mp.heic` is the same 4000 × 3000 camera image identified by hash in the
  original report, including its source ICC profile.

The tracked [input preparation scripts](../../../../benchmarks/Mediana.MediaComparison/README.md)
produce the authored image/page/Office inputs. Additional encodings use:

```sh
opj_compress -i photo-source.ppm -o photo-tiled.jp2 -t 512,512 -n 6 -p RPCL
cwebp -quiet -q 75 photo-source.ppm -o photo-lossy.webp
cwebp -quiet -lossless large-alpha.png -o photo-alpha-lossless.webp
ffmpeg -nostdin -v error -n -i big-buck-bunny-720p.mov -map 0:a:0 -ac 2 -c:a flac -sample_fmt s16 soundtrack.flac
ffmpeg -nostdin -v error -n -i big-buck-bunny-720p.mov -map 0:a:0 -c copy soundtrack.m4a
ffmpeg -nostdin -v error -n -stream_loop 3 -i big-buck-bunny-720p.mov -t 1800 -map 0:v:0 -map 0:a:0 -c copy -movflags +faststart video-30m.mp4
ffmpeg -nostdin -v error -n -stream_loop 3 -i soundtrack.flac -t 1800 -c:a libopus -b:a 64k soundtrack-30m.opus
```

Input hashes, rather than a reproduction with a different codec build, define the
recorded series. The source film is CC BY 3.0; its attribution and source identity
are in the linked source report.

## Output validation and interpretation

[validation.jsonl](validation.jsonl) contains complete sample/byte comparisons,
frame counts, source PSNR and copied AAC checks as appropriate.
[measurements.jsonl](measurements.jsonl) retains all selected successful runs and
[ranges.md](ranges.md) gives medians, ranges and encoded output byte counts.
[builds.json](builds.json) records executable fingerprints and build modes.

Every repeat fingerprints the complete output. Later byte-identical pairs may reuse
the first complete sample comparison within the same immutable-input case; the
record identifies that repetition. A validation success means the comparison
completed, not that lossy or rendered output is pixel-identical. Differences remain
explicit in the summary and records.

Video decoding includes per-frame SHA-256 of every native visible YUV plane.
The hash implementation differs between .NET and FFmpeg, so the timing is
**decode plus hashing**, not an isolated decoder throughput measurement.
All 43,201 frame hashes agree, covering 59,721,062,400 decoded bytes per process.
The manifests report 28,887 different frame timings. FFmpeg exports a 1/24 clock;
the source uses 1/19200 and its repeated-film joins fall between 24 Hz ticks.
Timestamp equality is not established by the pixel match.

Remux and HLS validation decodes every output picture against the original by
frame order, checks complete frame counts and fingerprints all copied AAC access
units with canonical ADTS headers. It includes every referenced HLS segment and
requires a finalized VOD playlist. Frame-order comparison does not prove exact
presentation-clock equality.

Video transcoding is video-only constrained-baseline software H.264 with a
10 Mbit/s target, 20 Mbit buffer and 48-picture GOP. Mediana uses QP 26;
libx264 uses `veryfast`. These are not matched algorithms or matched-quality
settings. The source PSNR and total output bytes must accompany its speed result;
a smaller or less accurate output does not establish a faster equivalent encoder.

## Accuracy and remaining work

- Both complete FLAC outputs match FFmpeg sample for sample. The 5.1 AAC output
  has the same 28,631,040 frames and 171,786,240 PCM16 samples, but 494,602 samples
  differ; maximum error is 11,291 and RMSE is 24.194438 PCM16 units. This is an
  accuracy issue requiring investigation, not an equivalent-output speed result.
- The complete MP3 downmix/resample produces 39,361,536 mono PCM16 samples.
  Maximum error is 356 and RMSE is 0.294830 PCM16 units. The resampling filters
  differ; this row includes MP3 decoding, mixing and output writing.
- All 177 lossless WebP animation canvases match exactly. TGS renders all 180
  frames, but its RGBA RMSE is 11.438445 and maximum byte difference is 255.
  Completing the frame sequence does not establish identical vector rendering.
- The JBIG2 MMR page matches every output pixel. The SVG scene and vector PDF
  have RGBA RMSE 0.896234 and 12.936392 respectively. Their text, antialiasing
  and effect paths need separate conformance work.
- Every XLSX numeric value matches the reference across all 400,000 cells.
  Ogg extraction preserves all 90,001 encoded packets and 14,387,962 payload
  bytes. Neither result establishes document layout or audio decoding accuracy.
- Baseline/progressive JPEG output has maximum byte error 3 and RGBA RMSE
  0.238809; lossy VP8 decoding has maximum error 1 and RMSE 0.059419. PNG,
  lossless WebP, LZW TIFF and reversible tiled JP2 match every RGBA byte.
- Both lossless WebP encoders reproduce every source component. Mediana takes
  1230ms versus 3509ms, but produces 8,962,318 bytes versus 7,703,882 bytes.
  For lossy quality setting 75, source RGBA RMSE is 4.361002 versus 4.753252;
  sizes are 1,505,268 versus 1,101,804 bytes. Equal quality-setting numbers do
  not establish matched quality or compression efficiency.
- The JPEG/soft-mask PDF has RGBA RMSE 10.190034. Word page and presentation
  slide RMSE are 50.849370 and 14.133231; page layout and rendering differ.
  The Word fixture sets explicit DejaVu Sans fonts without conflicting theme
  font attributes in its measured paragraph styles.
  Visual inspection shows missing Word table borders, taller table rows, a
  displaced photograph and lighter text. The slide has extra white dot markers
  inside otherwise empty chart rectangles; title strokes and chart shadows differ.
- Complete AVC transcoding takes a median 1340327ms versus 249075ms: 5.38 times
  the elapsed time. Peak memory is 157Mb versus 143Mb. Mediana's output is about 2% larger, with source PSNR
  45.34 dB versus 48.89 dB. This configuration has a speed and compression
  efficiency gap; it does not show a memory advantage.

The largest time gaps in this selection include the SVG effects scene, complete
TGS animation, long audio resampling and AVC encoding. AAC accuracy and document
layout differences require correctness work alongside performance work. These
measurements identify affected operations; they do not establish a profiled cause.

RMSE is measured over all compared samples, including alpha for RGBA. Image
errors use 8-bit component units; audio errors use signed PCM16 sample units.
PSNR uses every decoded YUV sample over the complete video in frame order.
The summary retains the quality observation beside each speed result so a
different rendering or lossy output is not presented as identical work.
