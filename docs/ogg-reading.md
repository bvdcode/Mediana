# Ogg reading

`OggReaderFactory` registers Ogg Opus and Vorbis in the container pipeline.
`OggReader.OpenAsync` supplies independent and sequential packet access, stream
configurations, native properties, UTF-8 comment tags and granule-based duration.
It borrows an immutable random-access source and preserves its sequential cursor.
Calls on a reader must not overlap. Encoded packet leases may outlive the reader.

Opening scans the complete file and verifies page checksums by default. It retains
codec headers and an allocator-backed packet index, without retaining compressed
audio. A single verified page is cached for packet reads. Packet assembly handles
continuations across pages, interleaving and empty packets. Failures during indexed
reads preserve cursors and can be retried; failed opening releases its index.

Each logical stream has a separate zero-based stream index. Chained links can
reuse serial numbers and remain separately selectable. Their local timelines are
preserved; selecting one stream does not concatenate other links. The container
duration is exposed when there is exactly one logical stream. Individual stream
durations remain available for multiplexed and chained sources.

Opus retains its 48 kHz media clock, pre-skip, output gain, input sample-rate
declaration and channel mapping. Vorbis configuration uses Xiph lacing for its three
headers. Setup inspection reads codebook structure, floors, residues, mappings and
packet block modes without expanding spectral vector tables. Neither container parser
decodes audio. Register `OpusDecoderFactory` or `VorbisDecoderFactory` for managed
PCM decoding; see [Opus decoding](opus-decoding.md) and
[Vorbis decoding](vorbis-decoding.md). Unsupported logical-stream codecs fail explicitly.

`FindSeekPoint` searches the retained packet index without source reads.
`GetPrerollStart` combines requested history with at least 80 ms for Opus and one
preceding packet for Vorbis. Raw packet durations retain coded samples;
`GetPresentationDuration` accounts for final granule trimming and Vorbis's first
overlap packet. Opus pre-skip remains a separate beginning-of-stream declaration.

`OggPacketReader` is the forward-only framing API for sequential sources. It
provides packets and page granules without interpreting codec syntax. Single-page
packets share immutable page storage; continued packets are assembled once on
completion. Page sequence, continuation, stream boundaries and CRC are checked
before their packets are published. A failed sequential read faults this reader.

The reference corpus checks every compressed packet and packet timestamp against
independent demuxing for stereo Opus and Vorbis. Additional tests cover maximum-size
pages, continuations, UTF-8 tags, repeated and reverse requests, chained links,
checksum failures, cancellation, preroll and allocation release.

Framing follows [RFC 3533](https://www.rfc-editor.org/rfc/rfc3533.html), Opus clocks
and headers follow [RFC 7845](https://www.rfc-editor.org/rfc/rfc7845.html), and Vorbis
setup and overlap follow the [Vorbis I specification](https://www.xiph.org/vorbis/doc/Vorbis_I_spec.html).
