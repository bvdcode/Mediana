`Mediana.Containers.Adts` reads standalone MPEG-2/MPEG-4 ADTS audio using the
common container and decoding APIs. Register `AdtsReaderFactory` alongside an
AAC decoder:

```csharp
MediaEngine engine = new MediaEngineBuilder()
    .AddContainer(new AdtsReaderFactory())
    .AddAudioDecoder(new AacDecoderFactory())
    .Build();
await using StreamByteSource source = new(File.OpenRead(path));
await using AudioSession session = await engine.OpenAudioAsync(source);
using AudioFrame? frame = await session.ReadFrameAsync();
```

For sequential sources, opening reads seven header bytes. For complete random-access
sources, the factory selects `AdtsIndexedReader`, which reads each seven-byte header
and skips the compressed payload and optional CRC words. Its index retains payload
positions and lengths. Indexed packet reads preserve both sequential cursors;
clock and seek-point queries perform no source reads. Decoder preroll uses the
existing packet-history contract. The total coded duration is exposed after
indexing; it is not reported as a container-declared duration.

The default `AdtsReaderOptions.PacketFraming` is `TransportFrame`: each packet
contains one complete ADTS frame, advertises `StreamFormats.AacAdts`, and retains
one to four raw data blocks. Packet reads allocate the current frame once. The
AAC decoder reconstructs each raw block as a separate `AudioFrame`, maintaining
core, SBR and PS history between them. At most four PCM frames can await output;
all block CRCs must pass before any output from the transport packet is returned.
The reader borrows the source; packets retain their storage after reader disposal.
Timestamps and durations account for 1024 core samples per raw block, preserving
codec priming and padding even when the number of blocks changes between packets.
Implicit SBR, when present, is a decoding concern; the ADTS header declares the
core sampling frequency. No duration is inferred from file size or bitrate.

Clients requiring one raw AAC access unit per packet can explicitly select
`AdtsPacketFraming.RawAccessUnit`. This mode rejects multi-block transport frames.
Unprotected reads allocate the raw payload; protected reads retain a nine-byte
`AdtsPacketProtection.SideDataKind` prefix and payload as immutable views of the
same frame allocation. It does not split or concatenate codec access units.

The header parser validates sync, layer, indexed frequency, reserved MPEG-2
profile and declared frame capacity. The reader detects truncated payloads and
configuration changes; transport reads also validate protected block positions
and their header CRC. Leading/interleaved metadata and resynchronization remain
open. The AAC decoder validates protected packets during its existing syntax
parse, before emitting PCM. Invalid protection faults the decoder and requires
reset or disposal. SBR decoding remains separate from ADTS transport support.

Demux clients can validate protection explicitly without reconstructing samples:

```csharp
IPacketIntegrityValidator validator = new AacDecoderFactory()
    .CreateIntegrityValidator(reader.Streams[0]);
PacketIntegrityStatus status = validator.Validate(packet, cancellationToken);
```

`Verified` refers to the validator's protection scheme, carried in the transport
frame or retained side data; `NotPresent` means that scheme is absent.
Malformed metadata, mismatched headers, truncated syntax and
CRC mismatches throw. The validator does not decode PCM or retain packet storage.
Packet `Retain` and `RetainWithDuration` preserve side-data ownership. Applications
constructing a packet from only `RetainData` intentionally retain only its payload.

ADTS CRC-16 uses polynomial 0x8005 and initial state 0xffff. A protected multi-block
frame has a header CRC covering its fixed/variable header and block-position
table, followed by a separate CRC for each raw block. Positions count bytes from
the first block and include preceding block CRC words. In single-block frames,
the one CRC combines all 56 transport header bits and the AAC syntax regions.
Raw-block coverage includes the first 192 bits of each supported channel
element, another 128 bits from the second CPE channel, and complete data-stream
elements. Element identifiers are excluded. Short channel regions are zero
padded; overlapping CPE bits are protected twice. Fill data and the complete AAC
payload are not blanket-protected. Coupling and in-band program configurations
remain unsupported by this AAC decoder and validator.

Tests cover byte-at-a-time sources, protected and unprotected headers, packet
timing and ownership, cancellation, overlapping operations, malformed lengths,
truncation and configuration changes. Two independent FFmpeg ADTS fixtures
decode through the public engine; every mono/stereo sample matches the existing
independent float32 references within 2e-6, with exact sample counts and clocks.
Indexed decoding also checks repeated backward and forward seeks against continuous
PCM and verifies that index creation reads headers without payloads.
Independent FDK AAC 2.0.3 fixtures validate all 24 original mono/stereo CRC words,
including short and overlapping channel regions. A further six-frame DSE vector
is accepted by the unchanged FDK decoder and covers data beyond the fixed
channel prefix length. Tests also reject header, CRC-word and coded-syntax
corruption, check budget/cancellation cleanup, and verify shared backing memory.
Mixed one-, two-, three- and four-block protected/unprotected vectors contain
18 complete access units each. The unchanged FDK AAC 2.0.3 decoder reconstructs
all 18 with identical PCM to their original single-block transport. Tests compare
every decoded sample, repeated out-of-order seeks, exact core timestamps and
header-only indexing. Regrouped mono/stereo SBR and PS sequences retain every
sample of their single-block reconstruction.
MPEG-TS copying preserves complete ADTS frames without adding another header
or allocating another payload buffer. Generic interval-copy planning accepts
complete timed audio transport packets; video still requires access-unit framing.
Fragmented MP4 requires raw AAC access units and rejects ADTS-framed copy.
An independently decoded complete MPEG-TS sequence contains all 18 access units;
every PCM16 sample differs from the corresponding Mediana WAVE conversion by
at most one least-significant bit using FFmpeg 6.1.1.
This does not establish coverage of every AAC profile or ADTS variant.

Header syntax follows [ISO/IEC 13818-7](https://www.iso.org/standard/43345.html).
CRC coverage follows ISO/IEC 14496-3:2001, Annex 1.A.3.2.1, pages 65–66.
Multi-block error-check syntax and positions follow
[ISO/IEC 14496-3:2001/Cor 1:2002](https://www.iso.org/standard/37603.html),
Annex 1.A.3.2, pages 53–54 and 58.
[SMPTE ST 2041-3](https://pub.smpte.org/pub/st2041-3/st2041-3-2010.pdf), sections
5–6, also describes ADTS carriage and the distinction between core and SBR output
sample rates.
