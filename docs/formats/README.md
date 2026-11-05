# Format implementation catalog

Mediana's target formats and independently tracked operations. This is a development map,
not a claim that every listed format is currently supported. Priorities and scope may evolve.

**469 variants · 1058 operations · 19 groups.**

**Existing implementations: 77 variants · 101 operations.** These counts include implemented subsets; exact scope is recorded below.

[How to use the checklist and verify completion](guide.md) · [Specifications](sources.md) ·
[Format definitions (CSV)](catalog.csv) · [Operation checklist (CSV)](checks.csv) · [Render.NET coverage and remaining modules](../rendering-coverage.md)

R = read structure/demux; D = decode; V = render; E = encode; W = write/mux/export.
□ planned; ◐ implemented subset; ✓ complete for the stated scope. A = core; B = expansion;
C = specialist/legacy exploration. These tiers do not prescribe a fixed development order.

The tables show implementation only. Samples, correctness, CPU, memory and Native AOT
have separate cells **for each operation** in `checks.csv`. An implementation mark
does not close these checks. Existing subsets remain ◐ until the row's scope is covered.

Update the two CSV sources, then run `python3 tools/format-catalog.py` from the repository root.
Verify the tables with `python3 tools/format-catalog.py --check`.

| Group | Implemented variants | Target variants | Implemented operations | Target operations |
| --- | ---: | ---: | ---: | ---: |
| [Containers and packaging](#containers-and-packaging) | 12 | 44 | 14 | 88 |
| [Elementary streams and packet representations](#elementary-streams-and-packet-representations) | 4 | 15 | 6 | 30 |
| [JPEG families and monochrome coding](#jpeg-families-and-monochrome-coding) | 8 | 23 | 8 | 46 |
| [Web images and modern still-image coding](#web-images-and-modern-still-image-coding) | 11 | 26 | 12 | 52 |
| [Raster files, archives and camera images](#raster-files-archives-and-camera-images) | 0 | 35 | 0 | 64 |
| [HDR, scientific images and raw sample layouts](#hdr-scientific-images-and-raw-sample-layouts) | 0 | 16 | 0 | 32 |
| [GPU texture coding](#gpu-texture-coding) | 0 | 14 | 0 | 28 |
| [Video codecs and substantial profiles](#video-codecs-and-substantial-profiles) | 4 | 62 | 5 | 124 |
| [Audio coding and sample representations](#audio-coding-and-sample-representations) | 12 | 63 | 18 | 128 |
| [Vector graphics and 2D animation](#vector-graphics-and-2d-animation) | 3 | 11 | 6 | 33 |
| [Documents, spreadsheets and presentations](#documents-spreadsheets-and-presentations) | 5 | 39 | 8 | 115 |
| [Fonts and text presentation resources](#fonts-and-text-presentation-resources) | 6 | 14 | 7 | 27 |
| [Subtitles, captions and timed graphics](#subtitles-captions-and-timed-graphics) | 0 | 13 | 0 | 39 |
| [Streaming presentations and transport protocols](#streaming-presentations-and-transport-protocols) | 2 | 13 | 2 | 26 |
| [Metadata, color and ancillary records](#metadata-color-and-ancillary-records) | 5 | 12 | 5 | 24 |
| [3D meshes and manufacturing interchange](#3d-meshes-and-manufacturing-interchange) | 5 | 14 | 10 | 41 |
| [3D scenes, materials and animation](#3d-scenes-materials-and-animation) | 0 | 20 | 0 | 60 |
| [CAD, BIM and engineering geometry](#cad-bim-and-engineering-geometry) | 0 | 18 | 0 | 54 |
| [Point clouds, geometry compression and volumes](#point-clouds-geometry-compression-and-volumes) | 0 | 17 | 0 | 47 |

## Containers and packaging

| ID | Format / variant | Extensions | Operations | Tier | Scope / sources |
| --- | --- | --- | --- | :---: | --- |
| `mp4-classic` | MP4 / ISO BMFF — Non-fragmented files | .mp4 .m4a .m4v | R ◐ · W □ | A | Current reader maps AVC/AAC/MP3; other codec mappings remain separate work. [iso-bmff](sources.md#iso-bmff) |
| `mp4-fragmented` | MP4 / ISO BMFF — Fragmented files | .mp4 .m4s | R □ · W □ | A | Includes initialization segments, fragment timing and incremental input. [iso-bmff](sources.md#iso-bmff) |
| `mov` | QuickTime — Movie files | .mov .qt | R ◐ · W □ | A | Current MP4-family reader covers a subset; legacy QuickTime sample descriptions remain open. [quicktime](sources.md#quicktime) |
| `3gp` | 3GPP — 3GP / 3G2 files | .3gp .3g2 | R □ · W □ | B | Validate their brands and track mappings independently of MP4. [iso-bmff](sources.md#iso-bmff), [discovery](sources.md#discovery) |
| `matroska-indexed` | Matroska — Indexed file presentation | .mkv .mka .mks | R ◐ · W □ | A | Current packet mappings are AVC/AAC/MP3, not all Matroska codecs. [matroska](sources.md#matroska) |
| `matroska-live` | Matroska — Live / unknown-size stream | .mkv | R □ · W □ | B | [matroska](sources.md#matroska) |
| `webm-file` | WebM — File presentation | .webm | R □ · W □ | A | Codec mappings and track constraints must conform to WebM. [webm](sources.md#webm) |
| `webm-live` | WebM — Incremental / live presentation | .webm | R □ · W □ | B | Track metadata, unknown sizes and incremental clusters need separate fixtures. [webm](sources.md#webm) |
| `ogg` | Ogg — Single and multiplexed logical streams | .ogg .oga .ogv .opus .spx | R □ · W □ | A | Codec-specific headers, granules and chaining are not interchangeable. [ogg](sources.md#ogg) |
| `ogg-chained` | Ogg — Chained logical streams | .ogg .opus | R □ · W □ | B | [ogg](sources.md#ogg) |
| `avi` | AVI — RIFF AVI 1.0 | .avi | R □ · W □ | B | [riff](sources.md#riff) |
| `avi-opendml` | AVI — OpenDML / AVI 2.0 | .avi | R □ · W □ | B | [riff](sources.md#riff), [discovery](sources.md#discovery) |
| `asf` | ASF — File and streaming structure | .asf .wmv .wma | R □ · W □ | B | [asf](sources.md#asf) |
| `flv` | FLV — Classic file structure | .flv | R □ · W □ | B | [flv](sources.md#flv) |
| `flv-enhanced` | Enhanced FLV — Extended codec mappings | .flv | R □ · W □ | C | Pin the extended specification and codec mappings first. [discovery](sources.md#discovery) |
| `mpeg-ts` | MPEG-TS — 188-byte transport packets | .ts .mpegts | R □ · W ◐ | A | Current writer: one program with AVC/AAC/MP3; reader and broader mappings are open. [mpeg-systems](sources.md#mpeg-systems) |
| `m2ts` | MPEG-TS — 192-byte source packets | .m2ts .mts | R □ · W □ | B | [mpeg-systems](sources.md#mpeg-systems), [discovery](sources.md#discovery) |
| `mpeg-ts-204` | MPEG-TS — 204-byte protected packets | .ts | R □ · W □ | C | [mpeg-systems](sources.md#mpeg-systems) |
| `mpeg-ps` | MPEG Program Stream — MPEG-1 / MPEG-2 systems | .mpg .mpeg .vob | R □ · W □ | B | [mpeg-systems](sources.md#mpeg-systems) |
| `mxf-op1a` | MXF — OP1a interleaved essence | .mxf | R □ · W □ | B | Essence mappings require their own codec and metadata fixtures. [mxf](sources.md#mxf) |
| `mxf-opatom` | MXF — OP-Atom essence | .mxf | R □ · W □ | C | [mxf](sources.md#mxf) |
| `gxf` | GXF — Broadcast exchange | .gxf | R □ · W □ | C | [discovery](sources.md#discovery) |
| `wave-riff` | WAVE — RIFF little-endian PCM / float | .wav .wave | R ◐ · W ◐ | A | Reader and writer exist; supported sample representations differ by direction. [riff](sources.md#riff) |
| `wave-extensible` | WAVE — WAVE_FORMAT_EXTENSIBLE | .wav .wave | R ◐ · W ◐ | A | Channel masks, valid bits and subformat GUIDs require coverage. [riff](sources.md#riff) |
| `wave-rifx` | WAVE — RIFX big-endian | .wav | R □ · W □ | B | [riff](sources.md#riff) |
| `wave-rf64` | RF64 / BW64 — Large WAVE files | .wav .rf64 | R □ · W □ | A | Handle ds64 sizes and long-duration streaming output. [bwf](sources.md#bwf) |
| `wave-bwf` | Broadcast WAVE — BWF metadata and timeline | .wav .bwf | R □ · W □ | B | [bwf](sources.md#bwf) |
| `wave-compressed` | WAVE — Compressed format-tag mappings | .wav | R □ · W □ | B | Container support is separate from ADPCM and other codecs. [riff](sources.md#riff) |
| `wave64` | Wave64 — 64-bit chunk format | .w64 | R □ · W □ | B | [discovery](sources.md#discovery) |
| `aiff` | AIFF — Big-endian integer PCM | .aif .aiff | R ◐ · W □ | B | Current sequential reader is mono/stereo. [aiff](sources.md#aiff) |
| `aifc` | AIFF-C — Compressed / alternate PCM mappings | .aifc .aiff | R ◐ · W □ | B | Current mappings: NONE/twos, supported sowt and float32/64; no general compressed AIFF-C decoder. [aiff](sources.md#aiff) |
| `caf` | Core Audio Format — Chunked audio container | .caf | R □ · W □ | B | [caf](sources.md#caf) |
| `au` | Sun / NeXT AU — Audio container | .au .snd | R □ · W □ | B | [discovery](sources.md#discovery) |
| `flac-native` | FLAC — Native file framing and metadata | .flac | R ◐ · W □ | A | FLAC codec operation is tracked separately. [flac](sources.md#flac) |
| `mp3-file` | MP3 — Layer III frame stream with metadata | .mp3 | R ◐ · W □ | A | Writer means framing/metadata and stream copy, not MP3 compression. [mpeg-audio](sources.md#mpeg-audio), [metadata](sources.md#metadata) |
| `heif-wrapper` | HEIF — Image item graph and derived items | .heif .heic | R ◐ · W □ | A | Current HEIC item graph includes grids, identity, overlays, transforms and alpha. [heif](sources.md#heif) |
| `avif-wrapper` | AVIF — HEIF AV1 image item mapping | .avif | R □ · W □ | A | Requires AV1 decoding/encoding in addition to item-graph support. [avif](sources.md#avif) |
| `jp2-wrapper` | JP2 — JPEG 2000 Part 1 file format | .jp2 | R ◐ · W □ | A | Current reader supports a subset of color and channel boxes. [jpeg2000](sources.md#jpeg2000) |
| `jpx-wrapper` | JPX — JPEG 2000 Part 2 file format | .jpx .jpf | R □ · W □ | B | Composition, multiple codestreams and additional color/channel mappings. [jpeg2000](sources.md#jpeg2000) |
| `mj2-wrapper` | Motion JPEG 2000 — JPEG 2000 Part 3 file format | .mj2 .mjp2 | R □ · W □ | C | [jpeg2000](sources.md#jpeg2000) |
| `jxl-wrapper` | JPEG XL — Part 2 boxed file format | .jxl | R □ · W □ | A | Boxes, metadata and codestream fragmentation are distinct from coding modes. [jpegxl](sources.md#jpegxl) |
| `ktx1-wrapper` | KTX — Version 1 texture container | .ktx | R □ · W □ | B | [ktx](sources.md#ktx) |
| `ktx2-wrapper` | KTX — Version 2 texture container | .ktx2 | R □ · W □ | B | Supercompression, levels, layers, faces and format descriptors are independent checks. [ktx](sources.md#ktx) |
| `dds-wrapper` | DDS — Legacy and DX10 texture headers | .dds | R □ · W □ | B | [bmp-dds](sources.md#bmp-dds) |

## Elementary streams and packet representations

| ID | Format / variant | Extensions | Operations | Tier | Scope / sources |
| --- | --- | --- | --- | :---: | --- |
| `avc-annexb` | AVC — Annex B byte stream | .h264 .264 | R □ · W ◐ | A | Current access-unit converter is not a complete arbitrary-stream demuxer. [h264](sources.md#h264) |
| `avc-length` | AVC — Length-prefixed NAL units / avcC | .avc | R ◐ · W ◐ | A | Configuration and packet syntax already shared by container and codec modules. [h264](sources.md#h264), [iso-bmff](sources.md#iso-bmff) |
| `hevc-annexb` | HEVC — Annex B byte stream | .h265 .265 .hevc | R □ · W □ | B | [h265](sources.md#h265) |
| `hevc-length` | HEVC — Length-prefixed NAL units / hvcC | .hevc | R □ · W □ | B | HEIC configuration parsing is not a complete video elementary-stream reader. [h265](sources.md#h265), [iso-bmff](sources.md#iso-bmff) |
| `vvc-annexb` | VVC — Annex B byte stream | .h266 .266 .vvc | R □ · W □ | C | [h266](sources.md#h266) |
| `av1-obu` | AV1 — Low-overhead OBU stream | .obu | R □ · W □ | A | [av1](sources.md#av1) |
| `av1-annexb` | AV1 — Annex B framing | .av1 | R □ · W □ | B | [av1](sources.md#av1) |
| `ivf` | IVF — Framed VP8/VP9/AV1 video | .ivf | R □ · W □ | B | [webm](sources.md#webm), [discovery](sources.md#discovery) |
| `aac-adts` | AAC — ADTS framing | .aac .adts | R □ · W ◐ | A | Existing access-unit conversion does not provide a standalone ADTS source reader. [mpeg-audio](sources.md#mpeg-audio) |
| `aac-adif` | AAC — ADIF framing | .aac | R □ · W □ | C | [mpeg-audio](sources.md#mpeg-audio) |
| `aac-latm` | AAC — LATM / LOAS framing | .latm .loas | R □ · W □ | B | [mpeg-audio](sources.md#mpeg-audio) |
| `aac-asc` | AAC — Raw access units / AudioSpecificConfig | — | R ◐ · W ◐ | A | Already used for MP4 AAC and AAC encoding; transport boundaries remain explicit. [mpeg-audio](sources.md#mpeg-audio) |
| `ac3-framing` | AC-3 / E-AC-3 — Syncframe streams | .ac3 .eac3 | R □ · W □ | B | [etsi-audio](sources.md#etsi-audio) |
| `dts-framing` | DTS — Core / extension stream framing | .dts | R □ · W □ | C | [discovery](sources.md#discovery) |
| `y4m` | YUV4MPEG2 — Frame stream with explicit geometry | .y4m | R □ · W □ | B | [discovery](sources.md#discovery) |

## JPEG families and monochrome coding

| ID | Format / variant | Extensions | Operations | Tier | Scope / sources |
| --- | --- | --- | --- | :---: | --- |
| `jpeg-baseline` | JPEG — Baseline sequential Huffman DCT, 8-bit | .jpg .jpeg .jpe | D ◐ · E □ | A | Current grayscale/three-component decoding; encoder is open. [jpeg](sources.md#jpeg) |
| `jpeg-sequential` | JPEG — Extended sequential Huffman DCT, 8/12-bit | .jpg .jpeg | D ◐ · E □ | B | Current SOF1 decoder is 8-bit; 12-bit precision remains open. [jpeg](sources.md#jpeg) |
| `jpeg-progressive` | JPEG — Progressive Huffman DCT, 8/12-bit | .jpg .jpeg | D ◐ · E □ | A | Current SOF2 decoder is 8-bit; no encoder or 12-bit reconstruction yet. [jpeg](sources.md#jpeg) |
| `jpeg-arithmetic` | JPEG — Arithmetic DCT coding | .jpg .jpeg | D □ · E □ | B | [jpeg](sources.md#jpeg) |
| `jpeg-lossless` | JPEG — Predictive lossless, Huffman/arithmetic | .jpg .jpeg | D □ · E □ | B | Original JPEG lossless is distinct from JPEG-LS and JPEG XL. [jpeg](sources.md#jpeg) |
| `jpeg-hierarchical` | JPEG — Hierarchical modes | .jpg .jpeg | D □ · E □ | C | [jpeg](sources.md#jpeg) |
| `jpeg-cmyk` | JPEG — Four-component CMYK / YCCK presentation | .jpg .jpeg | D □ · E □ | A | Color interpretation and conversion need independent verification. [jpeg](sources.md#jpeg), [metadata](sources.md#metadata) |
| `jpeg-mpo` | MPO — Multi-picture JPEG | .mpo | D □ · E □ | B | [jpeg](sources.md#jpeg), [metadata](sources.md#metadata), [discovery](sources.md#discovery) |
| `jpegls-lossless` | JPEG-LS — Part 1 lossless | .jls | D □ · E □ | B | [jpegls](sources.md#jpegls) |
| `jpegls-near` | JPEG-LS — Part 1 near-lossless | .jls | D □ · E □ | B | [jpegls](sources.md#jpegls) |
| `jpegls-extensions` | JPEG-LS — Part 2 extensions | .jls | D □ · E □ | C | [jpegls](sources.md#jpegls) |
| `j2k-reversible` | JPEG 2000 — Part 1 reversible 5/3 | .j2k .j2c .jpc | D ◐ · E □ | A | Current decoder covers integer components; packet/header extensions remain open. [jpeg2000](sources.md#jpeg2000) |
| `j2k-irreversible` | JPEG 2000 — Part 1 irreversible 9/7 | .j2k .j2c .jpc | D ◐ · E □ | A | Current tests include independent references with an explicit numeric tolerance. [jpeg2000](sources.md#jpeg2000) |
| `j2k-part2` | JPEG 2000 — Part 2 coding extensions | .j2k .jpx | D □ · E □ | B | [jpeg2000](sources.md#jpeg2000) |
| `htj2k` | HTJ2K — JPEG 2000 Part 15 block coding | .j2k .jhc .jph | D □ · E □ | B | Track HT block coding independently of JP2/JPX wrappers. [htj2k](sources.md#htj2k) |
| `jpegxr` | JPEG XR — HD Photo coding | .jxr .wdp .hdp | D □ · E □ | B | [jpegxr](sources.md#jpegxr) |
| `jpegxs` | JPEG XS — Low-latency coding profiles | .jxs | D □ · E □ | C | Split profiles when a concrete use case and conformance corpus are selected. [jpegxs](sources.md#jpegxs) |
| `jbig1` | JBIG — T.82 bi-level coding | .jbg .jbig | D □ · E □ | B | [jbig](sources.md#jbig) |
| `jbig2-sequential` | JBIG2 — Sequential standalone pages | .jb2 .jbig2 | D ◐ · E □ | A | Current decoder has bounded arithmetic/Huffman/MMR regions and dictionaries. [jbig](sources.md#jbig) |
| `jbig2-random` | JBIG2 — Random-access organization | .jb2 .jbig2 | D ◐ · E □ | B | [jbig](sources.md#jbig) |
| `jbig2-embedded` | JBIG2 — Embedded streams with globals | — | D ◐ · E □ | A | Used for PDF images; not a standalone-file wrapper. [jbig](sources.md#jbig), [pdf](sources.md#pdf) |
| `fax-t4` | CCITT Group 3 — T.4 one-/two-dimensional fax coding | — | D □ · E □ | B | [jbig](sources.md#jbig) |
| `fax-t6` | CCITT Group 4 — T.6 MMR coding | — | D □ · E □ | B | An internal JBIG2 primitive is not yet a general exposed image decoder. [jbig](sources.md#jbig) |

## Web images and modern still-image coding

| ID | Format / variant | Extensions | Operations | Tier | Scope / sources |
| --- | --- | --- | --- | :---: | --- |
| `png-gray` | PNG — Grayscale, 1/2/4/8/16-bit | .png | D ◐ · E □ | A | Current decode expands to RGBA8; native 16-bit output and grayscale encoding are open. [png](sources.md#png) |
| `png-rgb` | PNG — Truecolor RGB, 8/16-bit | .png | D ◐ · E □ | A | [png](sources.md#png) |
| `png-indexed` | PNG — Indexed color, 1/2/4/8-bit | .png | D ◐ · E □ | A | [png](sources.md#png) |
| `png-alpha` | PNG — Grayscale/truecolor with alpha, 8/16-bit | .png | D ◐ · E ◐ | A | Current encoder emits noninterlaced straight-alpha RGBA8 sRGB only. [png](sources.md#png) |
| `png-adam7` | PNG — Adam7 interlace | .png | D ◐ · E □ | A | [png](sources.md#png) |
| `apng` | APNG — Animated PNG with frame disposal/blending | .png .apng | D □ · E □ | A | [png](sources.md#png) |
| `webp-lossy` | WebP — Lossy VP8 image | .webp | D □ · E □ | A | [webp](sources.md#webp) |
| `webp-lossless` | WebP — Lossless VP8L image | .webp | D □ · E ◐ | A | Current encoder preserves straight RGBA8 sRGB exactly, including transparent RGB; decoding remains open. [webp](sources.md#webp) |
| `webp-extended` | WebP — Extended RIFF, alpha and metadata | .webp | D □ · E ◐ | A | Lossless alpha and animation headers are written; lossy alpha, ICC, Exif and XMP remain open. [webp](sources.md#webp) |
| `webp-animation` | WebP — Animated extended WebP | .webp | D □ · E ◐ | A | Current writer emits timed full-canvas lossless replacement frames to a seekable stream; partial rectangles, blending and disposal remain open. [webp](sources.md#webp) |
| `avif-still` | AVIF — Still image, Baseline/Advanced profiles | .avif | D □ · E □ | A | Split profile/precision coverage as AV1 implementation grows. [avif](sources.md#avif), [av1](sources.md#av1) |
| `avif-alpha` | AVIF — Auxiliary alpha images | .avif | D □ · E □ | A | [avif](sources.md#avif) |
| `avif-hdr` | AVIF — 10/12-bit and HDR presentation | .avif | D □ · E □ | A | HDR decoding, color preservation and SDR tone mapping are distinct tasks. [avif](sources.md#avif), [av1](sources.md#av1) |
| `avif-sequence` | AVIF — Image sequences / animation | .avif .avifs | D □ · E □ | A | [avif](sources.md#avif) |
| `heic-main` | HEIC — HEVC Main still-image coding, 8-bit | .heic .heif | D ◐ · E □ | A | Current intra-only implementation; general inter-frame HEVC is separate. [heif](sources.md#heif), [h265](sources.md#h265) |
| `heic-main10` | HEIC — HEVC Main 10 still-image coding | .heic .heif | D ◐ · E □ | A | Current implementation reconstructs a supported 10-bit intra subset. [heif](sources.md#heif), [h265](sources.md#h265) |
| `heic-rext` | HEIC — Range-extension still images | .heic .heif | D ◐ · E □ | B | Current subset includes monochrome/4:2:2/4:4:4 at 8/10-bit; not general RExt conformance. [heif](sources.md#heif), [h265](sources.md#h265) |
| `heic-sequence` | HEIC — Timed image sequences | .heic .heif | D □ · E □ | B | [heif](sources.md#heif), [h265](sources.md#h265) |
| `heif-jpeg` | HEIF — JPEG-coded image items | .heif | D □ · E □ | B | [heif](sources.md#heif), [jpeg](sources.md#jpeg) |
| `heif-j2k` | HEIF — JPEG 2000-coded image items | .heif .hej2 | D □ · E □ | B | [heif](sources.md#heif), [jpeg2000](sources.md#jpeg2000) |
| `jxl-modular` | JPEG XL — Part 1 Modular coding | .jxl | D □ · E □ | A | Includes lossless integer coding; precision/channels and transforms need their own samples. [jpegxl](sources.md#jpegxl) |
| `jxl-vardct` | JPEG XL — Part 1 VarDCT coding | .jxl | D □ · E □ | A | [jpegxl](sources.md#jpegxl) |
| `jxl-raw` | JPEG XL — Raw codestream representation | .jxl | D □ · E □ | A | Distinct from the Part 2 boxed container. [jpegxl](sources.md#jpegxl) |
| `jxl-jpeg-reconstruction` | JPEG XL — Reversible JPEG recompression | .jxl | D □ · E □ | B | Verify exact reconstruction of the original supported JPEG representation. [jpegxl](sources.md#jpegxl) |
| `jxl-animation` | JPEG XL — Animation and frame composition | .jxl | D □ · E □ | B | [jpegxl](sources.md#jpegxl) |
| `jxl-extra` | JPEG XL — HDR, floating-point and extra channels | .jxl | D □ · E □ | B | [jpegxl](sources.md#jpegxl) |

## Raster files, archives and camera images

| ID | Format / variant | Extensions | Operations | Tier | Scope / sources |
| --- | --- | --- | --- | :---: | --- |
| `gif87a` | GIF — GIF87a still images | .gif | D □ · E □ | A | [gif](sources.md#gif) |
| `gif89a` | GIF — GIF89a transparency and animation | .gif | D □ · E □ | A | Disposal, palette changes, timing and loop metadata require independent samples. [gif](sources.md#gif) |
| `bmp-rgb` | BMP — Uncompressed palettes / RGB / bitfields | .bmp .dib | D □ · E □ | A | [bmp-dds](sources.md#bmp-dds) |
| `bmp-rle` | BMP — RLE4 / RLE8 | .bmp .dib | D □ · E □ | B | [bmp-dds](sources.md#bmp-dds) |
| `bmp-v5` | BMP — V4/V5 color and alpha | .bmp | D □ · E □ | B | [bmp-dds](sources.md#bmp-dds) |
| `tiff-strips` | TIFF — Classic TIFF, strips and tiles | .tif .tiff | D □ · E □ | A | Both byte orders, planar configurations, multipage and native precision. [tiff](sources.md#tiff) |
| `tiff-bigtiff` | BigTIFF — 64-bit offsets and sizes | .tif .tiff .btf | D □ · E □ | B | [tiff](sources.md#tiff), [discovery](sources.md#discovery) |
| `tiff-packbits` | TIFF — PackBits compression | .tif .tiff | D □ · E □ | A | [tiff](sources.md#tiff) |
| `tiff-lzw` | TIFF — LZW compression and predictors | .tif .tiff | D □ · E □ | A | [tiff](sources.md#tiff) |
| `tiff-deflate` | TIFF — Deflate compression and predictors | .tif .tiff | D □ · E □ | A | [tiff](sources.md#tiff) |
| `tiff-jpeg` | TIFF — JPEG compression mappings | .tif .tiff | D □ · E □ | B | Old and revised JPEG-in-TIFF representations need separate fixtures. [tiff](sources.md#tiff), [jpeg](sources.md#jpeg) |
| `tiff-fax` | TIFF — CCITT fax compression | .tif .tiff | D □ · E □ | A | [tiff](sources.md#tiff), [jbig](sources.md#jbig) |
| `tga` | TGA — Raw/RLE truecolor, indexed and alpha | .tga | D □ · E □ | B | [discovery](sources.md#discovery) |
| `pcx` | PCX — Palette / RGB RLE | .pcx | D □ · E □ | C | [discovery](sources.md#discovery) |
| `sgi` | SGI RGB — Raw/RLE planar images | .sgi .rgb .rgba | D □ · E □ | C | [discovery](sources.md#discovery) |
| `pbm` | Netpbm PBM — Plain/binary monochrome | .pbm | D □ · E □ | B | [pnm](sources.md#pnm) |
| `pgm` | Netpbm PGM — Plain/binary grayscale | .pgm | D □ · E □ | B | [pnm](sources.md#pnm) |
| `ppm` | Netpbm PPM — Plain/binary RGB | .ppm .pnm | D □ · E □ | B | [pnm](sources.md#pnm) |
| `pam` | Netpbm PAM — Arbitrary tuple images | .pam | D □ · E □ | B | [pnm](sources.md#pnm) |
| `qoi` | QOI — Lossless RGB/RGBA | .qoi | D □ · E □ | B | [qoi](sources.md#qoi) |
| `farbfeld` | farbfeld — 16-bit RGBA | .ff | D □ · E □ | C | [discovery](sources.md#discovery) |
| `psd` | Photoshop PSD — Composite, layers, masks and channels | .psd | D □ · E □ | B | Merged preview and full layer composition require independent coverage. [psd](sources.md#psd) |
| `psb` | Photoshop PSB — Large-document structure | .psb | D □ · E □ | C | [psd](sources.md#psd) |
| `ora` | OpenRaster — Layered image package | .ora | D □ · E □ | B | [discovery](sources.md#discovery) |
| `xcf` | GIMP XCF — Layered image revisions | .xcf | D □ · E □ | C | [discovery](sources.md#discovery) |
| `icns` | Apple ICNS — Multi-resolution icons | .icns | D □ · E □ | B | [discovery](sources.md#discovery) |
| `ico` | Windows ICO / CUR — Multi-image icons and cursors | .ico .cur | D □ · E □ | A | Embedded PNG and DIB entries are distinct payloads. [bmp-dds](sources.md#bmp-dds), [discovery](sources.md#discovery) |
| `ani` | Windows ANI — Animated cursors | .ani | D □ · E □ | C | [riff](sources.md#riff), [discovery](sources.md#discovery) |
| `dng` | Digital Negative — DNG raw and rendered previews | .dng | D □ · E □ | B | Raw sample decode, demosaic/color processing and embedded previews are different operations. [tiff](sources.md#tiff) |
| `raw-cr2` | Canon RAW — CR2 | .cr2 | D □ | C | Camera-specific unpacking, metadata and color calibration need sourced coverage. [discovery](sources.md#discovery) |
| `raw-cr3` | Canon RAW — CR3 / CRX | .cr3 | D □ | C | [discovery](sources.md#discovery) |
| `raw-nef` | Nikon RAW — NEF / NRW | .nef .nrw | D □ | C | [discovery](sources.md#discovery) |
| `raw-arw` | Sony RAW — ARW / SR2 | .arw .sr2 | D □ | C | [discovery](sources.md#discovery) |
| `raw-raf` | Fujifilm RAW — RAF Bayer / X-Trans | .raf | D □ | C | [discovery](sources.md#discovery) |
| `raw-other` | Camera RAW — ORF / RW2 / PEF / SRW families | .orf .rw2 .pef .srw | D □ | C | Split by documented coding process and camera family before implementation. [discovery](sources.md#discovery) |

## HDR, scientific images and raw sample layouts

| ID | Format / variant | Extensions | Operations | Tier | Scope / sources |
| --- | --- | --- | --- | :---: | --- |
| `exr-scanline` | OpenEXR — Scanline images | .exr | D □ · E □ | B | [openexr](sources.md#openexr) |
| `exr-tiled` | OpenEXR — Tiled, mipmapped and ripmapped images | .exr | D □ · E □ | B | [openexr](sources.md#openexr) |
| `exr-multipart` | OpenEXR — Multipart and multiview images | .exr | D □ · E □ | C | [openexr](sources.md#openexr) |
| `exr-deep` | OpenEXR — Deep samples | .exr | D □ · E □ | C | [openexr](sources.md#openexr) |
| `exr-compression` | OpenEXR — PIZ / ZIP / PXR24 / B44 / DWA families | .exr | D □ · E □ | B | Split each accepted compression family into its own row before implementation. [openexr](sources.md#openexr) |
| `radiance-hdr` | Radiance HDR — RGBE / XYZE images | .hdr .pic | D □ · E □ | B | [discovery](sources.md#discovery) |
| `pfm` | Portable FloatMap — Floating-point gray/RGB | .pfm | D □ · E □ | B | [pnm](sources.md#pnm) |
| `dpx` | DPX — Digital motion-picture samples | .dpx | D □ · E □ | C | [mxf](sources.md#mxf), [discovery](sources.md#discovery) |
| `cineon` | Cineon — Logarithmic film samples | .cin | D □ · E □ | C | [discovery](sources.md#discovery) |
| `fits` | FITS — Astronomical image arrays | .fits .fit .fts | D □ · E □ | C | [discovery](sources.md#discovery) |
| `raw-rgb` | Raw RGB — RGB/BGR packed integer samples | .rgb .bgr .raw | D □ · E □ | B | Requires explicit dimensions, stride, precision and component order. [gpu-textures](sources.md#gpu-textures) |
| `raw-rgba` | Raw RGBA — RGBA/BGRA and alpha conventions | .rgba .bgra .raw | D □ · E □ | B | [gpu-textures](sources.md#gpu-textures) |
| `raw-yuv-planar` | Raw YUV — Planar 4:2:0 / 4:2:2 / 4:4:4 | .yuv | D □ · E □ | A | Stored frame planes already exist; complete raw-file import/export remains open. [h264](sources.md#h264), [gpu-textures](sources.md#gpu-textures) |
| `raw-yuv-semi` | Raw YUV — NV12 / NV21 / P010 / P016 | .yuv | D □ · E □ | B | [gpu-textures](sources.md#gpu-textures) |
| `raw-yuv-packed` | Raw YUV — YUY2 / UYVY / v210 | .yuv | D □ · E □ | B | [gpu-textures](sources.md#gpu-textures) |
| `raw-float` | Raw raster — Half/float/double component arrays | .raw | D □ · E □ | B | [gpu-textures](sources.md#gpu-textures) |

## GPU texture coding

| ID | Format / variant | Extensions | Operations | Tier | Scope / sources |
| --- | --- | --- | --- | :---: | --- |
| `bc1` | BC1 / DXT1 — RGB / one-bit alpha blocks | .dds .ktx .ktx2 | D □ · E □ | B | [gpu-textures](sources.md#gpu-textures) |
| `bc2` | BC2 / DXT3 — Explicit alpha blocks | .dds .ktx .ktx2 | D □ · E □ | B | [gpu-textures](sources.md#gpu-textures) |
| `bc3` | BC3 / DXT5 — Interpolated alpha blocks | .dds .ktx .ktx2 | D □ · E □ | B | [gpu-textures](sources.md#gpu-textures) |
| `bc4` | BC4 — One-component signed/unsigned blocks | .dds .ktx .ktx2 | D □ · E □ | B | [gpu-textures](sources.md#gpu-textures) |
| `bc5` | BC5 — Two-component signed/unsigned blocks | .dds .ktx .ktx2 | D □ · E □ | B | [gpu-textures](sources.md#gpu-textures) |
| `bc6h` | BC6H — HDR RGB blocks | .dds .ktx .ktx2 | D □ · E □ | B | [gpu-textures](sources.md#gpu-textures) |
| `bc7` | BC7 — RGBA block coding | .dds .ktx .ktx2 | D □ · E □ | B | [gpu-textures](sources.md#gpu-textures) |
| `etc1` | ETC1 — RGB blocks | .pkm .ktx | D □ · E □ | B | [gpu-textures](sources.md#gpu-textures) |
| `etc2-eac` | ETC2 / EAC — RGB/RGBA and one-/two-channel blocks | .ktx .ktx2 | D □ · E □ | B | [gpu-textures](sources.md#gpu-textures) |
| `astc-ldr` | ASTC — LDR block coding | .astc .ktx .ktx2 | D □ · E □ | B | [gpu-textures](sources.md#gpu-textures) |
| `astc-hdr` | ASTC — HDR block coding | .astc .ktx .ktx2 | D □ · E □ | C | [gpu-textures](sources.md#gpu-textures) |
| `pvrtc` | PVRTC — PVRTC1 / PVRTC2 | .pvr .ktx | D □ · E □ | C | [gpu-textures](sources.md#gpu-textures), [discovery](sources.md#discovery) |
| `basis-etc1s` | Basis Universal — ETC1S / BasisLZ | .basis .ktx2 | D □ · E □ | B | Decode/transcode and container supercompression are distinct boundaries. [ktx](sources.md#ktx), [gltf](sources.md#gltf) |
| `basis-uastc` | Basis Universal — UASTC | .basis .ktx2 | D □ · E □ | B | [ktx](sources.md#ktx), [gltf](sources.md#gltf) |

## Video codecs and substantial profiles

| ID | Format / variant | Extensions | Operations | Tier | Scope / sources |
| --- | --- | --- | --- | :---: | --- |
| `avc-constrained-baseline` | H.264 / AVC — Constrained Baseline | .h264 .264 | D ◐ · E ◐ | A | Current decoder subset and fixed-QP I/P encoder; complete profile coverage remains open. [h264](sources.md#h264) |
| `avc-baseline` | H.264 / AVC — Baseline | .h264 .264 | D ◐ · E □ | A | Includes tools beyond the constrained profile; do not inherit its completion. [h264](sources.md#h264) |
| `avc-main` | H.264 / AVC — Main | .h264 .264 | D ◐ · E □ | A | Current progressive 8-bit 4:2:0 CABAC/CAVLC subset includes I/P/B pictures. [h264](sources.md#h264) |
| `avc-extended` | H.264 / AVC — Extended | .h264 .264 | D □ · E □ | B | [h264](sources.md#h264) |
| `avc-high` | H.264 / AVC — High | .h264 .264 | D ◐ · E □ | A | Current progressive 8-bit 4:2:0 reconstruction uses default scaling matrices. [h264](sources.md#h264) |
| `avc-high10` | H.264 / AVC — High 10 / High 10 Intra | .h264 .264 | D □ · E □ | B | Parsing profile identifiers is not 10-bit decoding support. [h264](sources.md#h264) |
| `avc-high422` | H.264 / AVC — High 4:2:2 / High 4:2:2 Intra | .h264 .264 | D □ · E □ | B | [h264](sources.md#h264) |
| `avc-high444` | H.264 / AVC — High 4:4:4 Predictive / Intra | .h264 .264 | D □ · E □ | B | [h264](sources.md#h264) |
| `avc-cavlc444` | H.264 / AVC — CAVLC 4:4:4 Intra | .h264 .264 | D □ · E □ | C | [h264](sources.md#h264) |
| `avc-interlaced` | H.264 / AVC — Field pictures / PAFF / MBAFF | .h264 .264 | D □ · E □ | B | Tool-specific coverage across applicable profiles. [h264](sources.md#h264) |
| `avc-svc` | H.264 / AVC — Scalable extension profiles | .h264 | D □ · E □ | C | [h264](sources.md#h264) |
| `avc-mvc` | H.264 / AVC — Multiview / stereo extension profiles | .h264 | D □ · E □ | C | [h264](sources.md#h264) |
| `avc-3d` | H.264 / AVC — Depth and 3D extensions | .h264 | D □ · E □ | C | [h264](sources.md#h264) |
| `hevc-main` | H.265 / HEVC — Main, 8-bit 4:2:0 video | .h265 .hevc | D □ · E □ | A | HEIC intra reconstruction does not close general I/P/B video decoding. [h265](sources.md#h265) |
| `hevc-main10` | H.265 / HEVC — Main 10 | .h265 .hevc | D □ · E □ | A | [h265](sources.md#h265) |
| `hevc-main-still` | H.265 / HEVC — Main Still Picture | .h265 .hevc | D □ · E □ | B | Current HEIC intra path provides a subset; standalone video-codec integration remains open. [h265](sources.md#h265) |
| `hevc-rext` | H.265 / HEVC — Range extensions / 12-bit / 4:2:2 / 4:4:4 | .h265 .hevc | D □ · E □ | B | Split concrete RExt profiles when selecting implementation work. [h265](sources.md#h265) |
| `hevc-scc` | H.265 / HEVC — Screen-content extensions | .h265 .hevc | D □ · E □ | B | [h265](sources.md#h265) |
| `hevc-shvc` | H.265 / HEVC — Scalable extensions | .h265 .hevc | D □ · E □ | C | [h265](sources.md#h265) |
| `hevc-multiview` | H.265 / HEVC — Multiview / 3D extensions | .h265 .hevc | D □ · E □ | C | [h265](sources.md#h265) |
| `vvc-main10` | H.266 / VVC — Main 10 | .h266 .vvc | D □ · E □ | B | [h266](sources.md#h266) |
| `vvc-main444` | H.266 / VVC — Main 10 4:4:4 | .h266 .vvc | D □ · E □ | C | [h266](sources.md#h266) |
| `vvc-multilayer` | H.266 / VVC — Multilayer and still-picture profiles | .h266 .vvc | D □ · E □ | C | Split the selected profiles before implementation. [h266](sources.md#h266) |
| `vp8` | VP8 — Video bitstream | .ivf | D □ · E □ | A | WebP lossy image coding shares tools but has a separate file/alpha/animation contract. [webm](sources.md#webm) |
| `vp9-p0` | VP9 — Profile 0, 8-bit 4:2:0 | .ivf | D □ · E □ | A | [webm](sources.md#webm) |
| `vp9-p1` | VP9 — Profile 1, 8-bit other sampling | .ivf | D □ · E □ | B | [webm](sources.md#webm) |
| `vp9-p2` | VP9 — Profile 2, 10/12-bit 4:2:0 | .ivf | D □ · E □ | A | [webm](sources.md#webm) |
| `vp9-p3` | VP9 — Profile 3, 10/12-bit other sampling | .ivf | D □ · E □ | B | [webm](sources.md#webm) |
| `av1-main` | AV1 — Main profile | .obu .ivf | D □ · E □ | A | [av1](sources.md#av1) |
| `av1-high` | AV1 — High profile | .obu .ivf | D □ · E □ | B | [av1](sources.md#av1) |
| `av1-professional` | AV1 — Professional profile | .obu .ivf | D □ · E □ | B | [av1](sources.md#av1) |
| `av1-scalable` | AV1 — Operating points and scalable streams | .obu .ivf | D □ · E □ | B | Film grain, super-resolution and temporal/spatial layers need explicit tool coverage. [av1](sources.md#av1) |
| `av2` | AV2 — Versioned AOM bitstream | .obu | D □ · E □ | C | Pin the published bitstream revision and profile before implementation. [av2](sources.md#av2) |
| `mpeg1-video` | MPEG-1 Video — ISO/IEC 11172-2 | .m1v .mpg | D □ · E □ | B | [mpeg-video](sources.md#mpeg-video) |
| `mpeg2-main` | MPEG-2 Video — Simple/Main profiles | .m2v .mpg | D □ · E □ | B | [mpeg-video](sources.md#mpeg-video) |
| `mpeg2-high` | MPEG-2 Video — 4:2:2 / High / scalable profiles | .m2v | D □ · E □ | C | [mpeg-video](sources.md#mpeg-video) |
| `mpeg4-simple` | MPEG-4 Visual — Simple profile | .m4v | D □ · E □ | B | [mpeg-video](sources.md#mpeg-video) |
| `mpeg4-asp` | MPEG-4 Visual — Advanced Simple profile | .m4v | D □ · E □ | B | Includes familiar DivX/Xvid-era interoperability; product names are not new standards. [mpeg-video](sources.md#mpeg-video) |
| `h261` | H.261 — Video coding | .h261 | D □ · E □ | C | [itu-video](sources.md#itu-video) |
| `h263` | H.263 — Baseline | .h263 | D □ · E □ | C | [itu-video](sources.md#itu-video) |
| `h263-extensions` | H.263 — Annex extensions / H.263+ | .h263 | D □ · E □ | C | [itu-video](sources.md#itu-video) |
| `theora` | Theora — Video coding | .ogv | D □ · E □ | B | [theora](sources.md#theora) |
| `vc1` | VC-1 — Simple/Main/Advanced profiles | — | D □ · E □ | C | Select the SMPTE 421M revision and transport mappings before implementation. [discovery](sources.md#discovery) |
| `wmv-legacy` | Windows Media Video — Earlier WMV revisions | — | D □ · E □ | C | Split WMV1/WMV2 and concrete bitstream revisions when scoped. [discovery](sources.md#discovery) |
| `vp6` | VP6 — Legacy web video | — | D □ · E □ | C | [discovery](sources.md#discovery) |
| `vp7` | VP7 — Legacy video | — | D □ · E □ | C | [discovery](sources.md#discovery) |
| `avs-family` | AVS — AVS1 / AVS2 / AVS3 families | — | D □ · E □ | C | These are separate standards; split them before implementation. [discovery](sources.md#discovery) |
| `prores-422` | ProRes — 422 Proxy/LT/standard/HQ | — | D □ · E □ | B | Profiles share a family; preserve chroma/precision and select normative bitstream sources. [prores](sources.md#prores) |
| `prores-4444` | ProRes — 4444 / 4444 XQ with alpha | — | D □ · E □ | B | [prores](sources.md#prores) |
| `dnxhd` | DNxHD — HD editing profiles | — | D □ · E □ | C | [mxf](sources.md#mxf), [discovery](sources.md#discovery) |
| `dnxhr` | DNxHR — Resolution-independent profiles | — | D □ · E □ | C | [mxf](sources.md#mxf), [discovery](sources.md#discovery) |
| `dv` | DV — DV25 / DV50 / DV100 | — | D □ · E □ | C | [discovery](sources.md#discovery) |
| `ffv1-v1` | FFV1 — Version 1 archival coding | — | D □ · E □ | B | [ffv1](sources.md#ffv1) |
| `ffv1-v3` | FFV1 — Version 3, slices and integrity checks | — | D □ · E □ | B | [ffv1](sources.md#ffv1) |
| `huffyuv` | HuffYUV — Lossless video | — | D □ · E □ | C | [discovery](sources.md#discovery) |
| `utvideo` | Ut Video — Lossless video families | — | D □ · E □ | C | [discovery](sources.md#discovery) |
| `ffvhuff` | FFVHUFF — HuffYUV-derived lossless video | — | D □ · E □ | C | [discovery](sources.md#discovery) |
| `mjpeg` | Motion JPEG — Independently JPEG-coded video frames | — | D □ · E □ | B | Frame coding and AVI/MOV mappings must be tested separately. [jpeg](sources.md#jpeg), [mpeg-video](sources.md#mpeg-video) |
| `mjpeg2000` | Motion JPEG 2000 — Independently JPEG 2000-coded frames | — | D □ · E □ | C | [jpeg2000](sources.md#jpeg2000) |
| `vc2` | VC-2 / Dirac — Wavelet video | — | D □ · E □ | C | [discovery](sources.md#discovery) |
| `cineform` | CineForm — Wavelet editing codec | — | D □ · E □ | C | [discovery](sources.md#discovery) |
| `rawvideo` | Raw video — Uncompressed frame sequences | — | D □ · E □ | B | Pixel-layout rows define exact bytes; video timing still comes from a container or explicit options. [gpu-textures](sources.md#gpu-textures) |

## Audio coding and sample representations

| ID | Format / variant | Extensions | Operations | Tier | Scope / sources |
| --- | --- | --- | --- | :---: | --- |
| `pcm-u8` | PCM — Unsigned 8-bit | — | D ◐ · E ◐ | A | Current PCM reading/conversion and WAVE output paths; not a general audio encoder factory. [riff](sources.md#riff) |
| `pcm-s16` | PCM — Signed 16-bit, LE/BE | — | D ◐ · E ◐ | A | Both byte orders and native frame conversion need explicit fixtures. [riff](sources.md#riff), [aiff](sources.md#aiff) |
| `pcm-s24` | PCM — Packed signed 24-bit, LE/BE | — | D ◐ · E □ | A | Current decoding accepts supported 24-bit input; WAVE writer has no packed-24 output. [riff](sources.md#riff), [aiff](sources.md#aiff) |
| `pcm-s32` | PCM — Signed 32-bit, LE/BE | — | D ◐ · E ◐ | A | [riff](sources.md#riff), [aiff](sources.md#aiff) |
| `pcm-validbits` | PCM — Valid bits within aligned storage | — | D ◐ · E □ | B | Includes non-byte-aligned precision and extensible WAVE valid-bit declarations. [riff](sources.md#riff), [aiff](sources.md#aiff) |
| `pcm-f32` | PCM — IEEE float32, LE/BE | — | D ◐ · E ◐ | A | [riff](sources.md#riff), [aiff](sources.md#aiff) |
| `pcm-f64` | PCM — IEEE float64, LE/BE | — | D ◐ · E ◐ | B | [riff](sources.md#riff), [aiff](sources.md#aiff) |
| `g711-alaw` | G.711 — A-law | — | D □ · E □ | B | [itu-audio](sources.md#itu-audio) |
| `g711-mulaw` | G.711 — Mu-law | — | D □ · E □ | B | [itu-audio](sources.md#itu-audio) |
| `adpcm-ima-wave` | IMA ADPCM — WAVE block layout | — | D □ · E □ | B | [riff](sources.md#riff), [discovery](sources.md#discovery) |
| `adpcm-ima-qt` | IMA ADPCM — QuickTime block layout | — | D □ · E □ | B | [quicktime](sources.md#quicktime), [discovery](sources.md#discovery) |
| `adpcm-ms` | Microsoft ADPCM — WAVE block layout | — | D □ · E □ | B | [riff](sources.md#riff), [discovery](sources.md#discovery) |
| `adpcm-g726` | G.726 — ADPCM bit-rate modes | — | D □ · E □ | C | [itu-audio](sources.md#itu-audio) |
| `g722` | G.722 — Wideband sub-band coding | — | D □ · E □ | C | [itu-audio](sources.md#itu-audio) |
| `mp1` | MPEG Audio — MPEG-1/2 Layer I | .mp1 | D □ · E □ | B | [mpeg-audio](sources.md#mpeg-audio) |
| `mp2` | MPEG Audio — MPEG-1/2 Layer II | .mp2 | D □ · E □ | B | [mpeg-audio](sources.md#mpeg-audio) |
| `mp3-mpeg1` | MP3 — MPEG-1 Layer III | .mp3 | D ◐ · E □ | A | Current decoder covers mono/stereo/joint stereo; no MP3 encoder. [mpeg-audio](sources.md#mpeg-audio) |
| `mp3-mpeg2` | MP3 — MPEG-2 Layer III lower sampling rates | .mp3 | D ◐ · E □ | A | [mpeg-audio](sources.md#mpeg-audio) |
| `mp3-mpeg25` | MP3 — MPEG-2.5 Layer III extension | .mp3 | D ◐ · E □ | B | Widely used non-ISO extension; track it separately from MPEG-2. [mpeg-audio](sources.md#mpeg-audio), [discovery](sources.md#discovery) |
| `aac-lc` | AAC — Low Complexity, 1024/960-sample modes | — | D ◐ · E ◐ | A | Current decode: supported 1024-sample configurations 1–7; encode: experimental mono/stereo 1024 only. [mpeg-audio](sources.md#mpeg-audio) |
| `aac-main` | AAC — Main object type | — | D □ · E □ | C | [mpeg-audio](sources.md#mpeg-audio) |
| `aac-ssr` | AAC — Scalable Sample Rate | — | D □ · E □ | C | [mpeg-audio](sources.md#mpeg-audio) |
| `aac-ltp` | AAC — Long Term Prediction | — | D □ · E □ | C | [mpeg-audio](sources.md#mpeg-audio) |
| `heaac-v1` | HE-AAC — AAC-LC plus SBR | — | D □ · E □ | A | [mpeg-audio](sources.md#mpeg-audio) |
| `heaac-v2` | HE-AAC v2 — SBR plus Parametric Stereo | — | D □ · E □ | A | [mpeg-audio](sources.md#mpeg-audio) |
| `aac-ld` | AAC-LD — Low Delay | — | D □ · E □ | B | [mpeg-audio](sources.md#mpeg-audio) |
| `aac-eld` | AAC-ELD — Enhanced Low Delay | — | D □ · E □ | B | [mpeg-audio](sources.md#mpeg-audio) |
| `usac` | USAC / xHE-AAC — Unified speech and audio coding | — | D □ · E □ | B | [mpeg-audio](sources.md#mpeg-audio) |
| `mpeg-surround` | MPEG Surround — Spatial audio coding extension | — | D □ · E □ | C | [mpeg-audio](sources.md#mpeg-audio) |
| `mpegh-audio` | MPEG-H 3D Audio — Channel/object/scene-based audio | — | D □ · E □ | C | [mpeg-audio](sources.md#mpeg-audio) |
| `flac-codec` | FLAC — Lossless sample coding | .flac | D ◐ · E □ | A | Current decoder has fixed/LPC prediction, Rice residuals, stereo decorrelation and CRC checks. [flac](sources.md#flac) |
| `alac` | Apple Lossless — ALAC sample coding | — | D □ · E □ | A | [alac](sources.md#alac) |
| `opus-silk` | Opus — SILK speech mode | — | D □ · E □ | A | [opus](sources.md#opus) |
| `opus-celt` | Opus — CELT transform mode | — | D □ · E □ | A | [opus](sources.md#opus) |
| `opus-hybrid` | Opus — Hybrid and mode transitions | — | D □ · E □ | A | [opus](sources.md#opus) |
| `opus-multistream` | Opus — Multistream and channel mappings | — | D □ · E □ | B | Ambisonics mappings need their own declared scope and reference fixtures. [opus](sources.md#opus) |
| `vorbis` | Vorbis — Vorbis I mono/stereo/multichannel | — | D □ · E □ | A | [vorbis](sources.md#vorbis) |
| `speex-nb` | Speex — Narrowband | — | D □ · E □ | B | [speex](sources.md#speex) |
| `speex-wb` | Speex — Wideband / ultra-wideband | — | D □ · E □ | B | [speex](sources.md#speex) |
| `wavpack-lossless` | WavPack — Lossless coding | .wv | D □ · E □ | B | [wavpack](sources.md#wavpack) |
| `wavpack-hybrid` | WavPack — Hybrid lossy/correction streams | .wv .wvc | D □ · E □ | B | [wavpack](sources.md#wavpack) |
| `ape` | Monkey's Audio — APE revisions and compression modes | .ape | D □ · E □ | B | [discovery](sources.md#discovery) |
| `tak` | TAK — Lossless coding | .tak | D □ · E □ | C | [discovery](sources.md#discovery) |
| `tta` | True Audio — Lossless coding | .tta | D □ · E □ | C | [discovery](sources.md#discovery) |
| `shorten` | Shorten — Lossless coding | .shn | D □ · E □ | C | [discovery](sources.md#discovery) |
| `mlp` | MLP — Lossless multichannel coding | — | D □ · E □ | C | [discovery](sources.md#discovery) |
| `truehd` | Dolby TrueHD — MLP-derived HD coding | — | D □ · E □ | C | [discovery](sources.md#discovery) |
| `ac3` | AC-3 — Dolby Digital | — | D □ · E □ | B | [etsi-audio](sources.md#etsi-audio) |
| `eac3` | E-AC-3 — Dolby Digital Plus | — | D □ · E □ | B | [etsi-audio](sources.md#etsi-audio) |
| `dts-core` | DTS — Core coding | — | D □ · E □ | B | [discovery](sources.md#discovery) |
| `dts-hd` | DTS-HD — High-resolution / lossless extensions | — | D □ · E □ | C | [discovery](sources.md#discovery) |
| `amr-nb` | AMR — Narrowband | .amr | D □ · E □ | B | [etsi-audio](sources.md#etsi-audio) |
| `amr-wb` | AMR-WB — Wideband | .awb | D □ · E □ | B | [etsi-audio](sources.md#etsi-audio) |
| `gsm` | GSM Full Rate — Speech frames | .gsm | D □ · E □ | C | [discovery](sources.md#discovery) |
| `ilbc` | iLBC — Internet low-bit-rate speech | — | D □ · E □ | C | [discovery](sources.md#discovery) |
| `g729` | G.729 — CS-ACELP speech | — | D □ · E □ | C | [itu-audio](sources.md#itu-audio) |
| `wma` | Windows Media Audio — Standard coding revisions | — | D □ · E □ | C | [discovery](sources.md#discovery) |
| `wma-pro` | Windows Media Audio — Professional coding | — | D □ · E □ | C | [discovery](sources.md#discovery) |
| `wma-lossless` | Windows Media Audio — Lossless coding | — | D □ · E □ | C | [discovery](sources.md#discovery) |
| `dsd` | DSD — One-bit samples and channel layout | .dsf .dff | D □ · E □ | C | DSF/DFF wrappers and DSD-to-PCM processing require separate coverage. [discovery](sources.md#discovery) |
| `dst` | DST — Lossless DSD coding | — | D □ · E □ | C | [discovery](sources.md#discovery) |
| `midi-smf` | Standard MIDI File — Type 0/1/2 event sequences | .mid .midi | R □ · V □ · W □ | B | Playback requires an explicit synthesis/instrument-bank contract, not an audio decoder label. [discovery](sources.md#discovery) |
| `tracker-mod` | Tracker modules — MOD/XM/S3M/IT families | .mod .xm .s3m .it | R □ · V □ · W □ | C | Split formats before implementation; rendering includes sample mixing and effect semantics. [discovery](sources.md#discovery) |

## Vector graphics and 2D animation

| ID | Format / variant | Extensions | Operations | Tier | Scope / sources |
| --- | --- | --- | --- | :---: | --- |
| `svg-static` | SVG — Static SVG 1.1/2 scene subset | .svg | R ◐ · V ◐ · W □ | A | Current rasterizer covers paths, styles, text, masks, filters and embedded PNG; authoring/export is open. [svg](sources.md#svg) |
| `svg-compressed` | SVGZ — Gzip-compressed SVG | .svgz | R □ · V □ · W □ | B | [svg](sources.md#svg) |
| `svg-animation` | SVG — Declarative/CSS animation | .svg | R □ · V □ · W □ | B | Timeline evaluation is separate from static rendering. [svg](sources.md#svg), [html](sources.md#html) |
| `lottie-json` | Lottie — JSON 2D animation | .json .lottie.json | R ◐ · V ◐ · W □ | A | Current renderer uses a bounded 2D subset; no document writer. [lottie](sources.md#lottie) |
| `tgs` | TGS — Gzip Lottie sticker animation | .tgs | R ◐ · V ◐ · W □ | A | CLI can use stored expression values and omit Puppet with explicit warnings; these are not implemented effects. [lottie](sources.md#lottie) |
| `dotlottie` | dotLottie — Packaged animations and resources | .lottie | R □ · V □ · W □ | B | [lottie](sources.md#lottie), [discovery](sources.md#discovery) |
| `wmf` | Windows Metafile — 16-bit vector record stream | .wmf | R □ · V □ · W □ | B | [discovery](sources.md#discovery) |
| `emf` | Enhanced Metafile — EMF records | .emf | R □ · V □ · W □ | B | [discovery](sources.md#discovery) |
| `emf-plus` | EMF+ — Extended GDI+ records | .emf | R □ · V □ · W □ | B | [discovery](sources.md#discovery) |
| `postscript` | PostScript / EPS — Page description / encapsulated artwork | .ps .eps | R □ · V □ · W □ | C | Interpretation, fonts and execution/resource limits require a dedicated design. [discovery](sources.md#discovery) |
| `swf` | SWF — Timeline/vector media without application scripting | .swf | R □ · V □ · W □ | C | Define static/timeline scope separately from executable ActionScript content. [discovery](sources.md#discovery) |

## Documents, spreadsheets and presentations

| ID | Format / variant | Extensions | Operations | Tier | Scope / sources |
| --- | --- | --- | --- | :---: | --- |
| `pdf-1` | PDF — PDF 1.x / ISO 32000-1 page content | .pdf | R ◐ · V ◐ · W □ | A | Current parser/rendering covers a subset; current image extraction is not PDF writing. [pdf](sources.md#pdf) |
| `pdf-2` | PDF — PDF 2.0 / ISO 32000-2 | .pdf | R □ · V □ · W □ | B | [pdf](sources.md#pdf) |
| `pdf-a` | PDF/A — Archival conformance profiles | .pdf | R □ · V □ · W □ | B | Rendering a PDF/A file is distinct from validating or writing a conforming archival document. [pdf](sources.md#pdf) |
| `pdf-x` | PDF/X — Print exchange profiles | .pdf | R □ · V □ · W □ | B | [pdf](sources.md#pdf) |
| `pdf-forms` | PDF — AcroForm and annotation appearance | .pdf | R ◐ · V ◐ · W □ | B | Current renderer handles supported annotation appearances, not a complete form system. [pdf](sources.md#pdf) |
| `pdf-encrypted` | PDF — Standard encrypted documents | .pdf | R □ · V □ · W □ | B | Authentication/decryption is separate from signature verification and page rendering. [pdf](sources.md#pdf) |
| `pdf-3d` | PDF — Embedded U3D / PRC annotations | .pdf | R □ · V □ · W □ | C | Embedded geometry decoding and 3D annotation rendering remain separate tasks. [pdf](sources.md#pdf), [discovery](sources.md#discovery) |
| `openxml-opc` | Open Packaging Conventions — ZIP parts, content types and relationships | .docx .xlsx .pptx | R ◐ · W □ | A | Bounded package reading and content-type page decoder routing are implemented; document layout is registered separately. [office](sources.md#office) |
| `docx-transitional` | WordprocessingML — OOXML Transitional documents | .docx | R ◐ · V ◐ · W □ | A | Direct page rendering covers multiple sections, styles, text, tables, inline pictures and headers/footers; full Word layout and shaping remain open. [office](sources.md#office) |
| `docx-strict` | WordprocessingML — OOXML Strict documents | .docx | R ◐ · V □ · W □ | B | Reader accepts Strict namespaces; independent Strict document rendering coverage remains open. [office](sources.md#office) |
| `docm` | WordprocessingML — Macro-enabled package, static document content | .docm .dotm | R □ · V □ · W □ | B | Macro execution is not part of static rendering. [office](sources.md#office) |
| `dotx` | WordprocessingML — Templates | .dotx | R □ · V □ · W □ | B | [office](sources.md#office) |
| `xlsx-transitional` | SpreadsheetML — OOXML Transitional workbooks | .xlsx | R □ · V □ · W □ | A | Cell parsing, formula evaluation and styled print/page rendering are distinct capabilities. [office](sources.md#office) |
| `xlsx-strict` | SpreadsheetML — OOXML Strict workbooks | .xlsx | R □ · V □ · W □ | B | [office](sources.md#office) |
| `xlsm` | SpreadsheetML — Macro-enabled workbook, static content | .xlsm .xltm | R □ · V □ · W □ | B | Macro execution is separate from workbook rendering. [office](sources.md#office) |
| `xlsb` | Excel binary workbook — BIFF12 in OPC package | .xlsb | R □ · V □ · W □ | B | [office](sources.md#office) |
| `xltx` | SpreadsheetML — Workbook templates | .xltx | R □ · V □ · W □ | B | [office](sources.md#office) |
| `pptx-transitional` | PresentationML — OOXML Transitional presentations | .pptx | R □ · V □ · W □ | A | Theme/layout/master inheritance, shapes, text, charts and media need separate fixtures. [office](sources.md#office) |
| `pptx-strict` | PresentationML — OOXML Strict presentations | .pptx | R □ · V □ · W □ | B | [office](sources.md#office) |
| `pptm` | PresentationML — Macro-enabled presentation, static content | .pptm .potm | R □ · V □ · W □ | B | [office](sources.md#office) |
| `potx` | PresentationML — Templates and slideshow packages | .potx .ppsx | R □ · V □ · W □ | B | [office](sources.md#office) |
| `pptx-animation` | PresentationML — Slide transitions and animation timing | .pptx | R □ · V □ · W □ | B | Static slide rendering does not close timed presentation rendering. [office](sources.md#office) |
| `doc-binary` | Word binary — Legacy DOC revisions | .doc .dot | R □ · V □ · W □ | B | [office](sources.md#office) |
| `xls-binary` | Excel binary — BIFF5 / BIFF8 workbooks | .xls .xlt | R □ · V □ · W □ | B | [office](sources.md#office) |
| `ppt-binary` | PowerPoint binary — Legacy presentation revisions | .ppt .pps .pot | R □ · V □ · W □ | B | [office](sources.md#office) |
| `rtf` | Rich Text Format — Formatted text and embedded resources | .rtf | R □ · V □ · W □ | B | [office](sources.md#office), [discovery](sources.md#discovery) |
| `odt` | OpenDocument Text — ODF text documents | .odt .ott .fodt | R □ · V □ · W □ | B | [odf](sources.md#odf) |
| `ods` | OpenDocument Spreadsheet — ODF workbooks | .ods .ots .fods | R □ · V □ · W □ | B | [odf](sources.md#odf) |
| `odp` | OpenDocument Presentation — ODF presentations | .odp .otp .fodp | R □ · V □ · W □ | B | [odf](sources.md#odf) |
| `odg` | OpenDocument Graphics — ODF drawings | .odg .otg .fodg | R □ · V □ · W □ | B | [odf](sources.md#odf) |
| `xps` | XPS — Microsoft XPS document packages | .xps | R □ · V □ · W □ | B | [xps](sources.md#xps), [discovery](sources.md#discovery) |
| `openxps` | OpenXPS — ECMA-388 packages | .oxps | R □ · V □ · W □ | B | [xps](sources.md#xps) |
| `epub-reflow` | EPUB — Reflowable EPUB 2/3 publications | .epub | R □ · V □ · W □ | B | [epub](sources.md#epub) |
| `epub-fixed` | EPUB — Fixed-layout publications | .epub | R □ · V □ · W □ | B | [epub](sources.md#epub) |
| `fb2` | FictionBook — FB2 / zipped FB2 | .fb2 .fb2.zip | R □ · V □ · W □ | C | [discovery](sources.md#discovery) |
| `html-static` | HTML / CSS — Static document and print layout | .html .htm .xhtml | R □ · V □ · W □ | B | Explicitly define supported layout, resource and pagination features before implementation. [html](sources.md#html) |
| `markdown` | Markdown — Document text and defined dialects | .md .markdown | R □ · V □ · W □ | C | [discovery](sources.md#discovery) |
| `plain-text` | Plain text — Unicode text layout | .txt | R □ · V □ · W □ | B | [html](sources.md#html), [opentype](sources.md#opentype) |
| `csv` | Delimited text — CSV / TSV tabular interchange | .csv .tsv | R □ · W □ | B | Styled spreadsheet/page rendering is not implied by parsing delimited values. [discovery](sources.md#discovery) |

## Fonts and text presentation resources

| ID | Format / variant | Extensions | Operations | Tier | Scope / sources |
| --- | --- | --- | --- | :---: | --- |
| `font-truetype` | TrueType / OpenType — glyf outlines and metrics | .ttf .otf | R ◐ · W □ | A | Current outline/font-name subset is shared by drawing and document modules. [opentype](sources.md#opentype) |
| `font-cff1` | OpenType — CFF1 Type 2 outlines | .otf | R ◐ · W □ | A | Current name-keyed subset; CID-keyed coverage remains open. [opentype](sources.md#opentype) |
| `font-cff-cid` | OpenType / CFF — CID-keyed CFF1 fonts | .otf | R □ · W □ | B | [opentype](sources.md#opentype) |
| `font-cff2` | OpenType — CFF2 outlines | .otf | R □ · W □ | B | [opentype](sources.md#opentype) |
| `font-collection` | TrueType Collection — TTC / OTC multiple faces | .ttc .otc | R ◐ · W □ | B | Read: TrueTypeFont opens the first TTC face. Face enumeration, selection and broader TTC/OTC coverage remain open. [opentype](sources.md#opentype) |
| `font-variable` | OpenType — Variable axes and outline/metric variation | .ttf .otf | R □ · W □ | B | [opentype](sources.md#opentype) |
| `font-colr` | OpenType — COLR/CPAL color glyphs v0/v1 | .ttf .otf | R □ · W □ | B | [opentype](sources.md#opentype) |
| `font-bitmap` | OpenType — CBDT/CBLC, EBDT/EBLC and sbix glyphs | .ttf .otf | R □ · W □ | B | [opentype](sources.md#opentype) |
| `font-svg` | OpenType — SVG glyph documents | .ttf .otf | R □ · W □ | B | [opentype](sources.md#opentype), [svg](sources.md#svg) |
| `font-woff` | WOFF — Web font wrapper | .woff | R ◐ · W □ | B | Read: WOFF 1 table reconstruction, including raw and zlib-compressed tables, through TrueTypeFont. Wrapper conformance fixtures and writing remain open. [opentype](sources.md#opentype) |
| `font-woff2` | WOFF2 — Transformed/compressed web font wrapper | .woff2 | R □ · W □ | B | [opentype](sources.md#opentype) |
| `font-type1` | PostScript Type 1 — PFA / PFB font programs | .pfa .pfb | R □ · W □ | B | [pdf](sources.md#pdf), [discovery](sources.md#discovery) |
| `font-type3` | PDF Type 3 — Embedded glyph content streams | — | R ◐ | A | Current PDF subset draws glyph streams through the page renderer. [pdf](sources.md#pdf) |
| `font-shaping` | OpenType layout — GSUB/GPOS and script shaping | — | R ◐ · V ◐ | A | Read/render: GPOS kern pair positioning and legacy kern pairs, applied by SVG text. GSUB, script shaping and bidi remain open. [opentype](sources.md#opentype) |

## Subtitles, captions and timed graphics

| ID | Format / variant | Extensions | Operations | Tier | Scope / sources |
| --- | --- | --- | --- | :---: | --- |
| `srt` | SubRip — Timed text cues | .srt | R □ · V □ · W □ | A | [subtitles](sources.md#subtitles), [discovery](sources.md#discovery) |
| `webvtt` | WebVTT — Text cues, regions and styling | .vtt | R □ · V □ · W □ | A | [subtitles](sources.md#subtitles) |
| `ass` | ASS / SSA — Styled subtitle scripts | .ass .ssa | R □ · V □ · W □ | B | [subtitles](sources.md#subtitles), [discovery](sources.md#discovery) |
| `ttml` | TTML — TTML2 timed text | .ttml .xml | R □ · V □ · W □ | B | [subtitles](sources.md#subtitles) |
| `imsc` | IMSC — Text and image subtitle profiles | .ttml .xml | R □ · V □ · W □ | B | [subtitles](sources.md#subtitles) |
| `tx3g` | Timed Text — 3GPP / MP4 timed-text samples | — | R □ · V □ · W □ | B | [iso-bmff](sources.md#iso-bmff), [subtitles](sources.md#subtitles) |
| `mov-text` | QuickTime text — Legacy text samples | — | R □ · V □ · W □ | C | [quicktime](sources.md#quicktime) |
| `cea608` | CEA-608 — Line-21 closed captions | — | R □ · V □ · W □ | B | [subtitles](sources.md#subtitles), [discovery](sources.md#discovery) |
| `cea708` | CEA-708 — Digital television closed captions | — | R □ · V □ · W □ | B | [subtitles](sources.md#subtitles), [discovery](sources.md#discovery) |
| `dvb-subtitles` | DVB subtitles — Bitmap page/region composition | — | R □ · V □ · W □ | B | [subtitles](sources.md#subtitles), [discovery](sources.md#discovery) |
| `pgs` | HDMV PGS — Blu-ray presentation graphics | .sup | R □ · V □ · W □ | B | [subtitles](sources.md#subtitles), [discovery](sources.md#discovery) |
| `vobsub` | DVD VobSub — Bitmap subpictures and index | .idx .sub | R □ · V □ · W □ | B | [subtitles](sources.md#subtitles), [discovery](sources.md#discovery) |
| `ebu-stl` | EBU STL — Broadcast subtitle exchange | .stl | R □ · V □ · W □ | C | [subtitles](sources.md#subtitles), [discovery](sources.md#discovery) |

## Streaming presentations and transport protocols

| ID | Format / variant | Extensions | Operations | Tier | Scope / sources |
| --- | --- | --- | --- | :---: | --- |
| `hls-ts-vod` | HLS — VOD MPEG-TS media playlists | .m3u8 | R □ · W ◐ | A | Current writer supports stream copy and supported AVC/AAC encoding; no general HLS reader. [hls](sources.md#hls) |
| `hls-master` | HLS — Master playlists and rendition groups | .m3u8 | R □ · W ◐ | A | Current writer prepares supported video/audio variants and measured transport output. [hls](sources.md#hls) |
| `hls-live` | HLS — Live/event playlists and discontinuities | .m3u8 | R □ · W □ | A | [hls](sources.md#hls) |
| `hls-fmp4` | HLS — Fragmented MP4 segments | .m3u8 .m4s | R □ · W □ | A | [hls](sources.md#hls), [iso-bmff](sources.md#iso-bmff) |
| `hls-lowlatency` | HLS — Low-latency parts and blocking reload | .m3u8 | R □ · W □ | B | [hls](sources.md#hls) |
| `hls-encryption` | HLS — Declared encryption and key handling | .m3u8 | R □ · W □ | B | Encryption is separate from codec support and external rights-management systems. [hls](sources.md#hls) |
| `dash-static` | MPEG-DASH — Static MPD and segment addressing | .mpd | R □ · W □ | B | [dash](sources.md#dash) |
| `dash-dynamic` | MPEG-DASH — Dynamic presentations and updates | .mpd | R □ · W □ | B | [dash](sources.md#dash) |
| `cmaf` | CMAF — Fragmented media constraints and switching | .cmfv .cmfa .m4s | R □ · W □ | B | CMAF is a constrained media format, not another video codec. [iso-bmff](sources.md#iso-bmff), [dash](sources.md#dash), [hls](sources.md#hls) |
| `rtp-rtcp` | RTP / RTCP — Packet transport and media clocks | — | R □ · W □ | B | Each codec payload mapping and packetization mode needs its own fixtures. [rtp](sources.md#rtp) |
| `rtsp` | RTSP — Session control and media setup | — | R □ · W □ | C | [rtp](sources.md#rtp) |
| `rtmp` | RTMP — Chunked media transport | — | R □ · W □ | C | [flv](sources.md#flv), [discovery](sources.md#discovery) |
| `srt-transport` | SRT transport — Reliable low-latency transport | — | R □ · W □ | C | SRT transport is unrelated to SubRip .srt subtitle files. [discovery](sources.md#discovery) |

## Metadata, color and ancillary records

| ID | Format / variant | Extensions | Operations | Tier | Scope / sources |
| --- | --- | --- | --- | :---: | --- |
| `id3v2` | ID3v2 — Text, artwork and structured frames | — | R ◐ · W □ | A | Current MP3 reader exposes supported text and original pictures, not every frame/revision. [metadata](sources.md#metadata) |
| `id3v1` | ID3v1 — Legacy fixed-length tags | — | R ◐ · W □ | B | Read: Latin-1 ID3v1/v1.1 trailer fields after packet EOF; retained separately from ID3v2. Writing remains open. [metadata](sources.md#metadata) |
| `vorbis-comments` | Vorbis comments — Text metadata and picture mappings | — | R ◐ · W □ | A | Current native FLAC reader provides supported comments and pictures. [flac](sources.md#flac), [vorbis](sources.md#vorbis) |
| `mp4-metadata` | MP4 / QuickTime metadata — iTunes, indexed keys and artwork | — | R ◐ · W □ | A | Current movie-level text and original cover extraction; broader namespaces remain open. [iso-bmff](sources.md#iso-bmff), [quicktime](sources.md#quicktime) |
| `exif` | Exif — TIFF directories and presentation tags | — | R ◐ · W □ | A | Current JPEG parser applies primary orientation and supported color/aspect declarations. [metadata](sources.md#metadata) |
| `xmp` | XMP — Embedded and sidecar metadata | .xmp | R □ · W □ | B | [metadata](sources.md#metadata) |
| `iptc` | IPTC — IIM / image metadata mappings | — | R □ · W □ | B | [metadata](sources.md#metadata), [discovery](sources.md#discovery) |
| `icc` | ICC — v2/v4 profiles and color transforms | .icc .icm | R □ · W □ | A | Detecting or retaining a profile is not a color-management implementation. [metadata](sources.md#metadata) |
| `hdr-static` | HDR metadata — Mastering display and content light levels | — | R □ · W □ | B | [h265](sources.md#h265), [av1](sources.md#av1) |
| `hdr-dynamic` | HDR metadata — Dynamic metadata / gain maps | — | R □ · W □ | B | Split the selected metadata standards and mapping revisions before implementation. [h265](sources.md#h265), [avif](sources.md#avif), [jpegxl](sources.md#jpegxl), [metadata](sources.md#metadata) |
| `spherical` | Spherical media — 360-degree / projection / stereo metadata | — | R □ · W □ | C | [iso-bmff](sources.md#iso-bmff), [discovery](sources.md#discovery) |
| `timecode` | Timecode — Media timecode tracks and records | — | R □ · W □ | B | [quicktime](sources.md#quicktime), [mxf](sources.md#mxf) |

## 3D meshes and manufacturing interchange

| ID | Format / variant | Extensions | Operations | Tier | Scope / sources |
| --- | --- | --- | --- | :---: | --- |
| `obj` | Wavefront OBJ — Polygon geometry, attributes and groups | .obj | R ◐ · V ◐ · W □ | B | Indexed geometry and preview implemented; UVs are not retained, groups are combined and MTL/textures remain separate. [obj-ply](sources.md#obj-ply) |
| `mtl` | Wavefront MTL — Material library and texture references | .mtl | R □ · W □ | B | Interpret legacy material parameters explicitly when mapping to a renderer. [obj-ply](sources.md#obj-ply) |
| `stl-ascii` | STL — ASCII triangle mesh | .stl | R ◐ · V ◐ · W □ | B | Reader and CPU preview implemented; model writing remains open. [stl](sources.md#stl) |
| `stl-binary` | STL — Binary triangle mesh | .stl | R ◐ · V ◐ · W □ | B | Reader and CPU preview implemented; recognition checks exact length and triangle count. Facet color extensions are not rendered. [stl](sources.md#stl) |
| `ply-ascii` | PLY — ASCII elements and properties | .ply | R □ · V □ · W □ | B | Mesh, point and custom-property support need explicit scope. [obj-ply](sources.md#obj-ply) |
| `ply-binary-le` | PLY — Binary little-endian | .ply | R □ · V □ · W □ | B | [obj-ply](sources.md#obj-ply) |
| `ply-binary-be` | PLY — Binary big-endian | .ply | R □ · V □ · W □ | B | [obj-ply](sources.md#obj-ply) |
| `off` | OFF — Object File Format mesh variants | .off .coff .noff | R □ · V □ · W □ | C | [discovery](sources.md#discovery) |
| `3mf-core` | 3MF — Core meshes, objects and build items | .3mf | R ◐ · V ◐ · W □ | B | Geometry, root build, components, transforms and units implemented; material and texture interpretation remains separate. [3mf](sources.md#3mf) |
| `3mf-materials` | 3MF — Materials/properties and textures | .3mf | R □ · V □ · W □ | B | [3mf](sources.md#3mf) |
| `3mf-production` | 3MF — Production Extension model-part references and assemblies | .3mf | R ◐ · V ◐ · W □ | C | Cross-part component geometry implemented; other Production features require separate evidence. [3mf](sources.md#3mf) |
| `amf` | AMF — Additive manufacturing XML | .amf | R □ · V □ · W □ | C | [discovery](sources.md#discovery) |
| `3ds` | 3D Studio — Legacy chunked scenes | .3ds | R □ · V □ · W □ | C | [discovery](sources.md#discovery) |
| `lwo` | LightWave Object — LWO2 / related revisions | .lwo | R □ · V □ · W □ | C | [discovery](sources.md#discovery) |

## 3D scenes, materials and animation

| ID | Format / variant | Extensions | Operations | Tier | Scope / sources |
| --- | --- | --- | --- | :---: | --- |
| `gltf2-json` | glTF — 2.0 JSON with external/data resources | .gltf | R □ · V □ · W □ | B | Initial core coverage must distinguish geometry, PBR materials, cameras, skins, morphs and animation. [gltf](sources.md#gltf) |
| `gltf2-glb` | glTF — 2.0 binary GLB package | .glb | R □ · V □ · W □ | B | [gltf](sources.md#gltf) |
| `gltf2-materials` | glTF — Registered material/light extensions | .gltf .glb | R □ · V □ · W □ | B | Split transmission, volume, clearcoat, specular, emissive, texture transforms and lights when selected. [gltf](sources.md#gltf) |
| `gltf1` | glTF — Legacy 1.0 scenes | .gltf .glb | R □ · V □ · W □ | C | [gltf](sources.md#gltf), [discovery](sources.md#discovery) |
| `collada14` | COLLADA — 1.4 interchange documents | .dae | R □ · V □ · W □ | B | [collada](sources.md#collada) |
| `collada15` | COLLADA — 1.5 interchange documents | .dae | R □ · V □ · W □ | C | [collada](sources.md#collada) |
| `fbx-binary` | FBX — Binary scene revisions | .fbx | R □ · V □ · W □ | B | Managed implementation requires independently sourced file details; no native SDK dependency implied. [fbx](sources.md#fbx) |
| `fbx-ascii` | FBX — ASCII scene revisions | .fbx | R □ · V □ · W □ | B | [fbx](sources.md#fbx) |
| `usd-ascii` | USD — USDA text layers | .usda .usd | R □ · V □ · W □ | B | Layer parsing, composition and rendered scene features are separate acceptance scopes. [usd](sources.md#usd) |
| `usd-crate` | USD — USDC crate layers | .usdc .usd | R □ · V □ · W □ | B | [usd](sources.md#usd) |
| `usdz` | USDZ — Packaged scene/resources | .usdz | R □ · V □ · W □ | B | [usd](sources.md#usd) |
| `usd-composition` | USD — References, variants, payloads and instancing | .usd .usda .usdc | R □ · V □ · W □ | B | [usd](sources.md#usd) |
| `vrml1` | VRML — Version 1 | .wrl .vrml | R □ · V □ · W □ | C | [x3d](sources.md#x3d) |
| `vrml2` | VRML — VRML97 / version 2 | .wrl .vrml | R □ · V □ · W □ | C | [x3d](sources.md#x3d) |
| `x3d-xml` | X3D — XML scene encoding | .x3d | R □ · V □ · W □ | C | [x3d](sources.md#x3d) |
| `x3d-classic` | X3D — Classic VRML encoding | .x3dv | R □ · V □ · W □ | C | [x3d](sources.md#x3d) |
| `x3d-binary` | X3D — Compressed binary encoding | .x3db | R □ · V □ · W □ | C | [x3d](sources.md#x3d) |
| `blend` | Blender — Versioned .blend data blocks | .blend | R □ · V □ · W □ | C | Import/render/export scope is separate from supporting every application node or modifier. [blend](sources.md#blend) |
| `alembic-ogawa` | Alembic — Ogawa geometry cache | .abc | R □ · V □ · W □ | B | [alembic](sources.md#alembic) |
| `alembic-hdf5` | Alembic — Legacy HDF5 geometry cache | .abc | R □ · V □ · W □ | C | [alembic](sources.md#alembic) |

## CAD, BIM and engineering geometry

| ID | Format / variant | Extensions | Operations | Tier | Scope / sources |
| --- | --- | --- | --- | :---: | --- |
| `step-ap203` | STEP — AP203 product/assembly data | .step .stp | R □ · V □ · W □ | C | Exact geometry, topology, assemblies and tessellation are distinct tasks. [cad](sources.md#cad) |
| `step-ap214` | STEP — AP214 product data | .step .stp | R □ · V □ · W □ | C | [cad](sources.md#cad) |
| `step-ap242` | STEP — AP242 managed model-based engineering | .step .stp | R □ · V □ · W □ | B | [cad](sources.md#cad) |
| `step-xml` | STEP — Part 28 XML representation | .stpx .xml | R □ · V □ · W □ | C | [cad](sources.md#cad) |
| `iges` | IGES — Curves, surfaces and solids | .iges .igs | R □ · V □ · W □ | C | [cad](sources.md#cad) |
| `ifc2x3` | IFC — IFC2x3 building models | .ifc | R □ · V □ · W □ | C | [ifc](sources.md#ifc) |
| `ifc4` | IFC — IFC4 building models | .ifc | R □ · V □ · W □ | B | [ifc](sources.md#ifc) |
| `ifc43` | IFC — IFC4.3 infrastructure/building models | .ifc | R □ · V □ · W □ | B | [ifc](sources.md#ifc) |
| `ifcxml` | IFC — ifcXML / ifcZIP wrappers | .ifcxml .ifczip | R □ · V □ · W □ | C | [ifc](sources.md#ifc) |
| `dxf-ascii` | DXF — ASCII exchange drawing | .dxf | R □ · V □ · W □ | B | 2D drawings and 3D entities both need coverage; do not close on line parsing alone. [cad](sources.md#cad) |
| `dxf-binary` | DXF — Binary exchange drawing | .dxf | R □ · V □ · W □ | C | [cad](sources.md#cad) |
| `dwg` | DWG — Versioned native drawing family | .dwg | R □ · V □ · W □ | C | Exact revision and usable format documentation must be established first. [cad](sources.md#cad) |
| `jt` | JT — Tessellated/exact engineering data | .jt | R □ · V □ · W □ | C | [cad](sources.md#cad), [discovery](sources.md#discovery) |
| `3dxml` | 3DXML — Product structure and representation | .3dxml | R □ · V □ · W □ | C | [discovery](sources.md#discovery) |
| `parasolid` | Parasolid — X_T / X_B model exchange | .x_t .x_b | R □ · V □ · W □ | C | [cad](sources.md#cad), [discovery](sources.md#discovery) |
| `acis` | ACIS — SAT / SAB model exchange | .sat .sab | R □ · V □ · W □ | C | [cad](sources.md#cad), [discovery](sources.md#discovery) |
| `u3d` | U3D — Universal 3D streams | .u3d | R □ · V □ · W □ | C | [pdf](sources.md#pdf), [discovery](sources.md#discovery) |
| `prc` | PRC — Product representation compact | .prc | R □ · V □ · W □ | C | [pdf](sources.md#pdf), [discovery](sources.md#discovery) |

## Point clouds, geometry compression and volumes

| ID | Format / variant | Extensions | Operations | Tier | Scope / sources |
| --- | --- | --- | --- | :---: | --- |
| `las12` | LAS — 1.2 point records | .las | R □ · V □ · W □ | B | [points](sources.md#points) |
| `las14` | LAS — 1.4 point records and extended metadata | .las | R □ · V □ · W □ | B | [points](sources.md#points) |
| `laz` | LAZ — Lossless compressed LAS | .laz | R □ · V □ · W □ | B | Compressed point reconstruction and spatial preview are separate acceptance stages. [points](sources.md#points) |
| `e57` | E57 — 3D imaging/point exchange | .e57 | R □ · V □ · W □ | C | [points](sources.md#points) |
| `pcd-ascii` | PCD — ASCII point-cloud fields | .pcd | R □ · V □ · W □ | B | [points](sources.md#points) |
| `pcd-binary` | PCD — Binary / binary-compressed fields | .pcd | R □ · V □ · W □ | B | [points](sources.md#points) |
| `xyz-points` | XYZ / PTS / PTX — Text point-cloud families | .xyz .pts .ptx | R □ · V □ · W □ | C | Specify delimiters, attributes, units and coordinate conventions; split dialects as selected. [points](sources.md#points), [discovery](sources.md#discovery) |
| `draco-mesh` | Draco — Compressed triangular meshes | .drc | D □ · E □ | B | [geometry-codecs](sources.md#geometry-codecs), [gltf](sources.md#gltf) |
| `draco-points` | Draco — Compressed point clouds | .drc | D □ · E □ | B | [geometry-codecs](sources.md#geometry-codecs) |
| `meshopt` | Meshopt — Vertex/index/attribute compression | — | D □ · E □ | B | glTF buffer-view compression is separate from scene parsing and rendering. [geometry-codecs](sources.md#geometry-codecs), [gltf](sources.md#gltf) |
| `gaussian-ply` | Gaussian splats — PLY-based scene conventions | .ply | R □ · V □ · W □ | C | Pin attribute schema, spherical harmonics, opacity and ordering; no universal PLY splat standard implied. [geometry-codecs](sources.md#geometry-codecs), [obj-ply](sources.md#obj-ply) |
| `spz` | SPZ — Compressed Gaussian splats | .spz | D □ · E □ | C | [geometry-codecs](sources.md#geometry-codecs) |
| `openvdb` | OpenVDB — Sparse volume grids | .vdb | R □ · V □ · W □ | C | [volumes](sources.md#volumes) |
| `nanovdb` | NanoVDB — Compact sparse volume grids | .nvdb | R □ · V □ · W □ | C | [volumes](sources.md#volumes) |
| `dicom-images` | DICOM — Image transfer syntaxes / multiframe objects | .dcm .dicom | R □ · V □ · W □ | C | Split transfer syntax, pixel reconstruction, modality transforms and image/volume presentation before implementation. [volumes](sources.md#volumes) |
| `nifti1` | NIfTI — NIfTI-1 volumes | .nii .nii.gz .hdr .img | R □ · V □ · W □ | C | [volumes](sources.md#volumes) |
| `nifti2` | NIfTI — NIfTI-2 volumes | .nii .nii.gz | R □ · V □ · W □ | C | [volumes](sources.md#volumes), [discovery](sources.md#discovery) |
