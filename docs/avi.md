# AVI input

`Mediana.Containers.Avi` reads known-length random-access RIFF/AVI sources and
OpenDML `AVIX` continuations. `AviReaderFactory` identifies content by its RIFF
signature; filenames do not select the reader. Register the factory with
`MediaEngineBuilder.AddContainer`.

The reader implements `IIndexedPacketTimingReader` and
`IIndexedPresentationTimingReader` and `IIndexedPacketPrefixReader`. Opening reads stream headers and indexes,
with allocator-backed packet tables. MJPEG, PCM, Microsoft MPEG-4 v3 and WMV2
payload is read only when a packet is requested. MPEG-4 Visual reads bounded picture-header prefixes
to build its presentation clock. Supported index representations are `idx1`
with movie-relative or file-relative addresses, OpenDML standard chunk indexes,
and super indexes. Each referenced packet's chunk identifier and length are
checked before publishing it. Indexless files require a scan of chunk headers;
encoded payload is skipped. Record lists and odd-byte padding are respected.
These structures follow Microsoft's [AVI RIFF reference](https://learn.microsoft.com/en-us/windows/win32/directshow/avi-riff-file-reference),
[legacy index](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/api/aviriff/ns-aviriff-avioldindex),
and [OpenDML indexes](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/api/aviriff/ns-aviriff-avimetaindex).

Stream clocks retain reduced `dwScale/dwRate` rationals and `dwStart` offsets.
Empty video chunks advance time and extend the previous picture's presentation
interval without allocating a synthetic picture. Per-track indexed access does
not alter sequential cursors. Sequential packets retain source interleaving.
Timing and seek queries read no compressed payload.

`MJPG` maps to the shared complete-picture `mjpeg` decoder representation. PCM
uses the existing WAVE configuration and sample decoder. Other compression
identifiers retain native configuration and chunk framing for inspection and
explicit client handling; they do not claim decoder support. Presentation times
for unsupported reordered video remain unknown rather than copying decode time.
Declared MPEG-4 Part 2 FourCCs use the shared [MPEG-4 Visual decoder](mpeg4-visual.md).
AVI chunk decode clocks remain distinct from exact reordered VOP presentation
clocks. Display duration can differ from a chunk's transport duration.

`MP43`, `DIV3` and `WMV2` map to separate [codec representations](mpeg4-visual.md#microsoft-mpeg-4-v3-and-wmv2)
with coded dimensions from `strf`. Their presentation clocks follow the AVI
index because these streams decode in display order.

PAL and NTSC DV25 Type-2 `dvsd` tracks use the [DV decoder](dv.md). Every DV video
packet is independently accessible. OpenDML `vprp` describes display geometry
and supplies a sample aspect ratio when the relevant terms are present.

AVI `FFV1` tracks use `Ffv1DecoderFactory` from `Mediana.Codecs.Ffv1`.
The decoder supports 8-bit planar 4:2:0, 4:2:2, 4:4:4 and 4:1:1 with
Golomb–Rice and range entropy coding. Versions 0 and 1 carry parameters in
the opening picture. Version 3 uses its external configuration and verifies
slice CRCs when present. Seeking restarts at an indexed FFV1 keyframe and
rebuilds entropy contexts through the requested picture. See [RFC 9043](https://www.rfc-editor.org/rfc/rfc9043.html).

DivX/Xvid packed bitstreams are normalized into one VOP per logical packet.
Opening structurally scans packed reference chunks with bounded scratch storage
to locate secondary VOPs. It retains only packet ranges and timing metadata,
without decoding or retaining the complete compressed movie. Only declared
transport placeholders with matching reference clocks and valid stuffing are
removed. Uncoded pictures that advance presentation are rejected explicitly.
Every split packet retains the original physical chunk extent for validation
when read. Ordinary MPEG-4 AVI retains prefix-only clock indexing.

Field-subdivided indexes and MJPEG packets containing several pictures are
rejected. The decoder's [JPEG picture requirements](mjpeg.md) also apply.

The independent interleaved MJPEG/PCM corpus verifies every packet's bytes,
stream, offset, timestamp and duration against FFprobe. The complete public
processing operation decodes both streams and encodes AVC/AAC to MPEG-TS.
Structural tests cover both legacy origins, standard/super indexes, indexless
files, record lists, fractional clocks, delayed starts, repeated pictures, short
reads, truncation and allocation cleanup.

The packed Xvid corpus also checks full decoding, reverse seeking to every
picture, previews and whole/interval AVC encoding against independent output
from the original AVI. Structural tests cover split-chunk extent validation,
placeholder clocks, display durations, cancellation and released index storage.

## Audiovisual HLS

The shared HLS audio preparation opens AVI through its registered reader and
decodes PCM through the same audio registry used by other media operations.
The CLI can encode MJPEG/PCM input as H.264/AAC HLS with several video sizes:

```text
mediana hls encode input.avi output-directory --audio-stream 1 --audio-codec Aac --segment-ms 6000 --widths 1280 640
```

Stream indices are selected from `mediana probe input.avi`. The independent
1.5-second MJPEG/PCM fixture produces two variants with three aligned
half-second segments each. All 12 pictures and 72,000 input PCM samples are
processed, with 1,024 AAC priming samples and 704 padding samples. FFmpeg reads
the master and both media playlists without errors. Managed and Native AOT
checks compare every prepared PCM sample and timestamp and verify buffer release.


## MP3 audio

WAVE format tag `0x0055` maps to complete Layer III frames. The reader normalizes
several frames in one chunk and frames spanning several chunks, including a
header split between chunks. It retains source chunk ranges and validates each
addressed chunk when reading a packet. Opening reads frame headers and builds
sample-exact timing without retaining compressed audio or decoding it.
Declared WAVE sample rate and channel count must match the frames. Coded timing
uses `1 / sample rate`, independently of the AVI byte or frame clock. Reservoir
back references determine the packet's restart indication; a container index
flag does not establish independent MP3 decoding.

The independent MJPEG/MP3 fixture matches every FFprobe packet hash. Structural
tests cover chunk sizes from one byte to the complete audio stream. HLS exports
have been independently decoded both with MP3 stream copy and with AAC encoding.

## Segment requests

`AvcHlsTranscodeSession` also accepts an indexed AVI reader, its registered
MJPEG/MPEG-4 decoder and explicit output preparation. PCM audio can be supplied
through `HlsAacEncoding`. Planning and initialization do not read compressed
packets. The MJPEG/PCM test requests half-second audiovisual windows in order
2, 0, 1, 2; each request reads only local payload and required audio warm-up,
releases its work and reproduces identical bytes when repeated.
