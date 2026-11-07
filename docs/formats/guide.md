# Using the format catalog

The [catalog](README.md) is an adaptable map of Mediana's intended coverage,
including images, audio, video, documents, 3D scenes and models. It is broader
than the current implementation. Adding an entry records a target, not a release
promise or a dependency decision. Start with a useful end-to-end operation and
expand its supported variants as real files, specifications and measurements require.

## What constitutes a row

Keep these concepts separate:

| Concept | Example | What it establishes |
| --- | --- | --- |
| Container | MP4, WebM, Matroska | Streams, indexing, timestamps, metadata and packet representation |
| Codec | AVC/H.264, AV1, Opus | Compressed sample syntax and reconstruction |
| Profile or coding mode | H.264 High 10, WebP lossless | A materially different set of coding tools or sample constraints |
| Framing | AVC Annex B, AAC ADTS | How access units and their configuration are carried |
| Document or scene | DOCX, SVG, glTF | Structure that must be interpreted before rendering |
| Companion data | ICC, MTL, font programs | Presentation information; opening the parent file does not imply support |

One row describes a separately testable implementation boundary. A JPEG XL raw
codestream and a boxed JPEG XL file have separate entries; Modular and VarDCT
coding are separate entries too. There is no standard named "short JPEG XL".
H.264 editions, profiles, levels and extensions are not a codec named "H.264 V2".
JPEG 2000 remains a standards family used in several domains; its Part 1,
extensions and HTJ2K coding tools deserve distinct coverage.

Container recognition is independent of the codecs allowed by that container.
WebM entries must be validated with WebM's permitted codec mappings; they do not
inherit every Matroska mapping. AVIF reuses HEIF structure and AV1 coding, but a
HEIC decoder alone does not decode AVIF. A `.png`, `.mp4` or `.ply` extension does
not identify every feature present in the file. Input detection uses content;
output selection must resolve an explicit supported representation.

## Directions and states

| Operation | Meaning |
| --- | --- |
| `read` / R | Parse structure, metadata, relationships, sample tables or demux encoded packets |
| `decode` / D | Reconstruct raster, video, audio or compressed geometry samples |
| `render` / V | Lay out and rasterize a document, vector scene, subtitle or 3D scene |
| `encode` / E | Compress reconstructed samples using the specified coding system |
| `write` / W | Serialize a document/model, mux streams or write a protocol presentation |

Image codec rows use D/E for complete image decoding/encoding, including the
necessary image-file syntax. Separate wrapper rows are included where the wrapper
is independently useful, such as JPEG 2000, JPEG XL and HEIF. Compressed geometry
has D/E; the scene that references it has R/V/W. Raw sample rows use D/E for
unpacking/packing their exact byte representation.

Only applicable directions appear. A missing operation is not an implicit feature
promise. For example, a PDF reader, page renderer and PDF writer are three tasks;
PDF is not treated as an audio/video encoder. Plain CSV has R/W, while spreadsheet
layout belongs to a document format. Adding another direction requires a new
`checks.csv` record, not another copy of the format definition.

| State | Meaning |
| --- | --- |
| `planned` / □ | Open task; no accepted evidence covering the operation |
| `partial` / ◐ | An implemented or verified subset; the row's full scope remains open |
| `complete` / ✓ | The declared scope is met, with linked evidence for this particular check |

The implementation, samples, correctness, CPU, memory and Native AOT cells are
independent. A working encoder does not close decoder checks. A successful probe
does not close decoding, and successful stream copy does not close encoding.
`complete` means the stated row scope, not every conceivable extension to a standard.
Narrow the description explicitly if the actual target is narrower than the profile.

The initial catalog marks existing implementations as `partial`, with links to
their module descriptions or fixture documentation. It does not promote existing
regression tests to full profile conformance. Historic performance measurements
are not imported as per-profile performance acceptance. Office package reading is
listed separately from Word, Excel and PowerPoint layout. STL/OBJ/3MF geometry
reading and previews have implemented subsets; other 3D entries remain targets.
The overview counts variants and operations with an implementation independently
of their remaining acceptance checks. Each existing mark links to its scope and
code or test evidence; code-only paths can have an implementation mark while their
sample and correctness checks remain planned. The
[rendering coverage comparison](../rendering-coverage.md) identifies additional
Render.NET features without treating source availability as Mediana support.

Priorities are deliberately coarse: A for core conversion/preview/distribution,
B for broader interoperability, C for specialist, archival or legacy work. These
are editable planning groups, not a mandatory order, schedule or support tier.
For formats with proprietary or incomplete descriptions, obtain the exact format
documentation before implementation. A discovery link is not a normative spec.

## Cross-cutting coverage

The following dimensions belong in sample manifests and acceptance reports. Avoid
creating every combination as another artificial format name. Split a catalog row
when a dimension introduces a substantial new parser, reconstruction process or
layout/rendering subsystem.

| Domain | Dimensions to cover |
| --- | --- |
| Raster | Sample precision and signedness; gray/RGB/CMYK/indexed; chroma subsampling; planar/interleaved data; endianness; alpha and premultiplication; odd dimensions; scan/tile/strip order; multiframe/multipage; progressive delivery; lossless/lossy modes |
| Video | Profile/level/tier; 8/10/12-bit; monochrome/4:2:0/4:2:2/4:4:4; I/P/B and reference reordering; slices/tiles/WPP; progressive/interlaced/fields; VFR; cropped/coded dimensions; random access; parameter changes |
| Color | Primaries, matrix, transfer and range; chroma location; ICC; PQ/HLG; static/dynamic HDR metadata; gain maps; alpha; display orientation; pixel aspect ratio; tone mapping as a separate processing capability |
| Audio | Rate and precision; channel count/order/layout; planar/interleaved data; frame sizes; CBR/VBR; encoder delay, priming and padding; gapless playback; discontinuities; multichannel and spatial metadata |
| Containers/transport | Seekable/nonseekable I/O; fragmented/unfragmented/live; timestamps and wraparound; indexes; multiple streams/programs; codec changes; metadata; edit lists; attachments; captions; truncation and cancellation |
| Documents | Strict/Transitional revisions; pagination; styles; themes; relationships; drawings; charts; tables; formulas and cached values; embedded objects; right-to-left and complex text; missing fonts; print settings; annotations; encryption |
| Fonts/text | Unicode and shaping; fallback as an explicit policy; kerning; bidi; ligatures; vertical text; variable axes; color glyphs; embedded/subset fonts; missing glyph diagnostics |
| 3D | Units and handedness; transforms; mesh topology; normals/tangents/UVs; textures and color space; PBR/material extensions; lights/cameras; instancing; skinning; morphs; animation; scene composition; external resources |
| CAD/points/volumes | Exact curves and surfaces versus tessellation; assemblies; tolerances; coordinate/reference systems; point attributes; spatial indexing; large coordinates; sparse grids; reconstruction/export fidelity |

Loading a mesh does not close 3D rendering. The first useful 3D milestone should
produce a deterministic preview with explicit camera, lighting, material and
resource-resolution policies. CAD surface tessellation, point-cloud rendering and
volume rendering are distinct capabilities, not incidental mesh import details.
Likewise, parsing XLSX cells does not close spreadsheet rendering or formula
evaluation; opening a PPTX slide does not close its layout and animation behavior.

## Samples and correctness

Every operation needs a versioned sample manifest containing the catalog ID,
source and redistribution license, SHA-256, specification revision, actual
profile/features, expected result and reference implementation/version. Keep
personal documents out of the public fixture corpus. Use redistributable real
files and small authored boundary cases; both are needed.

Include ordinary files, independent producers, all supported coding tools,
boundaries and changes of configuration, truncated/malformed input, hostile size
declarations, cancellation and budget exhaustion. A header that claims a profile
is insufficient: fixtures must exercise that profile's tools. Preserve native
precision in reference data rather than checking every decoder through RGBA8.

For lossless decoding compare complete samples exactly. For lossy decoding use
the standard's precision requirements and an explicitly justified tolerance,
including color and timing. For encoders, open the output in independent decoders
and test bitstream conformance and decoded quality. Encoding output need not be
byte-identical to another encoder. Documents and scenes need whole-page/frame
visual comparisons, geometry/text checks and multiple timestamps where animated.
Container round trips must preserve the promised packets, timing and metadata.

## CPU, memory and AOT

The [performance register](../performance/README.md) maps format rows to ordinary
and heavy whole-operation workloads, named alternatives and measured elapsed /
CPU / peak RSS columns. Its links appear at the end of each applicable format
row. Keep numerical results there: a 200 MP photograph, a long audio stream and
a 30-minute video are different experiments, even when they share one container.
`docs/performance/cases.csv` defines cases and `results.csv` holds measured
medians with their detailed evidence. The catalog generator checks and updates
both tables. An empty comparison cell is an open measurement, not zero cost.

Measure useful complete operations, including demux, decode, processing, encode
and finalization where those are part of the task. Also keep isolated codec/kernel
measurements to locate costs. Do not substitute one extracted frame for a complete
transcode. Use short deterministic fixtures for regression checks and longer
representative files for sustained throughput, including video around 30 minutes
when the tested operation supports it.

Record input hash, command and options, output validation, hardware/OS, runtime,
build mode, thread policy, repetitions and warm/cold conditions. Compare equivalent
work: dimensions, frame/sample count, color/precision, audio layout and resampling,
codec settings, rate/quality and CPU features. Report default-thread and fixed-thread
runs separately. Hardware acceleration is a separate baseline from managed CPU work.

Track elapsed time, total process CPU time, throughput and peak resident memory;
track allocations, owned live-buffer peaks and working-set evolution where available.
Do not present allocation counters as total RAM. Measure baseline and candidate on
the same corpus before accepting an optimization; retain correctness checks. There
is no universal percentage threshold that closes every format's CPU/RAM task.
Link the measurements and the justified budget/remaining gap for each operation.

An AOT check requires publishing and running an executable that exercises the
actual operation, its resources and output on the supported target platforms.
A successful managed build, project annotation or unrelated AOT smoke test is
insufficient. Keep resource limits, ownership, cancellation and untrusted-input
handling part of the implementation throughout development.

## Module boundaries and maintenance

The catalog is not a list of packages to create. Keep the existing separation of
I/O, containers, syntax, codecs, drawing, processing and execution. Reuse shared
syntax and surfaces rather than copying an existing decoder into every document
module. Document layout, scene import, tessellation and rendering should compose
through explicit contracts; the command-line application selects operations.
Do not add a monolithic format switch or dependencies from the core to concrete
formats. See [module contracts](../architecture.md).

`catalog.csv` is the source of format identity, grouping and scope. `checks.csv`
is the source of per-operation progress. Both are UTF-8 CSV with stable IDs; they
can be imported into a spreadsheet. Preserve IDs when renaming a display label.
When splitting a variant, create new IDs and distribute the existing evidence
explicitly. Do not copy a completion mark to a more capable profile by inheritance.

1. Add or adjust a format and its applicable operation records.
2. Link its specification in [sources](sources.md). Discovery-only entries need a
   precise specification before their implementation starts.
3. Update only the check cells covered by the linked evidence. Add the evidence
   path relative to this directory; separate multiple paths with semicolons.
4. Run `python3 tools/format-catalog.py`, then
   `python3 tools/format-catalog.py --check` to validate references and table parity.

The [FFmpeg format](https://ffmpeg.org/ffmpeg-formats.html) and
[codec](https://ffmpeg.org/ffmpeg-codecs.html) inventories are useful for finding
additional interoperability targets. They are not the specification or the
acceptance oracle for every format. This catalog intentionally does not claim to
enumerate every private camera, application or game format; expand it when there
is a concrete use case and sufficient format information.
