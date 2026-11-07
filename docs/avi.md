# AVI input

`Mediana.Containers.Avi` reads known-length random-access RIFF/AVI sources and
OpenDML `AVIX` continuations. `AviReaderFactory` identifies content by its RIFF
signature; filenames do not select the reader. Register the factory with
`MediaEngineBuilder.AddContainer`.

The reader implements `IIndexedPacketTimingReader` and
`IIndexedPresentationTimingReader`. Opening reads stream headers and indexes,
with allocator-backed packet tables. MJPEG and PCM payload is read only when a
packet is requested. MPEG-4 Visual also reads bounded picture-header prefixes
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
