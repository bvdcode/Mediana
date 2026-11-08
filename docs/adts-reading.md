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

Each packet read allocates only the current raw data block, excluding its ADTS
header and optional CRC word. The reader borrows
the source; returned packets retain their own storage after reader disposal.
Timestamps advance by 1024 core samples and preserve codec priming and padding.
Implicit SBR, when present, is a decoding concern; the ADTS header declares the
core sampling frequency. No duration is inferred from file size or bitrate.

The header parser validates sync, layer, indexed frequency, reserved MPEG-2
profile and declared frame capacity. The reader detects truncated payloads and
configuration changes. One raw data block per frame is supported. Multiple
blocks, leading/interleaved metadata and resynchronization remain
open. CRC words are consumed but not verified. SBR decoding remains separate
from ADTS transport support.

Tests cover byte-at-a-time sources, protected and unprotected headers, packet
timing and ownership, cancellation, overlapping operations, malformed lengths,
truncation and configuration changes. Two independent FFmpeg ADTS fixtures
decode through the public engine; every mono/stereo sample matches the existing
independent float32 references within 2e-6, with exact sample counts and clocks.
Indexed decoding also checks repeated backward and forward seeks against continuous
PCM and verifies that index creation reads headers without payloads.
This does not establish coverage of every AAC profile or ADTS variant.

Header syntax follows [ISO/IEC 13818-7](https://www.iso.org/standard/43345.html).
[SMPTE ST 2041-3](https://pub.smpte.org/pub/st2041-3/st2041-3-2010.pdf), sections
5–6, also describes ADTS carriage and the distinction between core and SBR output
sample rates.
