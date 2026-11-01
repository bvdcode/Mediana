# Matroska fixtures

`avc-aac-attachment.mkv` copies the authored AVC/AAC streams from
`../Mp4/avc-aac.mp4` and attaches `../Metadata/cover.png`, without re-encoding.
The video is 64 by 48 pixels. The attachment is declared as `image/png` and
retains the filename `cover.png`.

Creation with FFmpeg 6.1.1:

```sh
ffmpeg -v error -i tests/Mediana.Tests/Data/Mp4/avc-aac.mp4 \
  -map 0 -c copy -map_metadata -1 -metadata title='Matroska stream-copy fixture' \
  -attach tests/Mediana.Tests/Data/Metadata/cover.png \
  -metadata:s:t:0 mimetype=image/png -metadata:s:t:0 filename=cover.png \
  -f matroska tests/Mediana.Tests/Data/Matroska/avc-aac-attachment.mkv
```

The committed file is 19,043 bytes, SHA-256
`63f999ab20fc744304bbcb45b5287eb50a94420df5f6b816f2ef19896e5de87c`.
Matroska UIDs make regenerated files differ. The EBML header ends at byte 40;
EBMLVersion is 1, DocTypeVersion is 4 and DocTypeReadVersion is 2.
The internal header tests inspect that header without reading the Segment.

`avc-aac-attachment.packets.json` contains independent FFprobe packet sizes,
SHA-256 payload hashes, stored positions, key flags and presentation timestamps
for all 84 video/audio packets. The separately reported attached PNG is excluded
from that media packet list.

## AAC lacing

`aac-lace-xiph.mka`, `aac-lace-ebml.mka` and `aac-lace-fixed.mka` contain three
AAC access units in one SimpleBlock using the named lacing mode.
`aac-lace-group-ebml.mka` places the three units in a BlockGroup with an explicit
64-tick BlockDuration. The audio is mono, 48 kHz, using the original authored
AAC configuration and payloads. The retained track number is 2; the track's
FlagLacing is explicitly set to 1. The fixed-size variant repeats the first
192-byte unit three times. Each document declares a 1 ms TimestampScale.

The files contain an EBML header, Segment Info, one audio TrackEntry and one
Cluster. The Cluster timestamp and block-relative timestamp are zero. Each
`.packets.json` records the file SHA-256 and three independent packet hashes.
FFprobe 6.1.1 recovers every unit exactly; FFmpeg fully decodes all four files to
3,072 mono Float32 samples. Xiph, EBML and BlockGroup variants decode identically.

The block timestamp applies to the first laced frame. Subsequent timestamps
require track/codec timing; the framing tests check the stored timestamp and all
payload boundaries rather than assigning per-frame timestamps from the framing
alone. Eight-byte signed lace differences and the normative all-one VINT values
are also covered by synthetic framing tests.
