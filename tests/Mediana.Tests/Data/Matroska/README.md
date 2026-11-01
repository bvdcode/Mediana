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

## Metadata references

Each `.metadata.json` records the input SHA-256, native Info and TrackEntry
declarations, codec-private ranges and hashes, and FFprobe 6.1.1 stream facts.
The five documents contain six timed tracks. The PNG attachment is excluded
from that count and remains distinct from a video TrackEntry. Native track
numbers and UIDs retain their original values and declaration order.

The independently inspected values cover timestamp units, declared duration,
track defaults, codec identifiers, dimensions, audio parameters and configuration
payloads. Synthetic metadata tests additionally exercise field ordering, empty
defaults, UUID byte order, date precision, cropping, limits and cancellation.
The streamed CRC vector has 9,033 payload bytes and checksum `0x468261E0`,
independently calculated with zlib; every chunk partition must retain that value.

## Clock references

`clock-vectors.json` contains 1,645 independently calculated clock expressions.
Python's `fractions.Fraction.from_float` interprets the exact IEEE 754 value of
each track scale. The reference evaluates
`cluster * segment_scale + relative * track_scale * segment_scale - codec_delay`
plus any rational contiguous-frame offset, then rounds once to the nearest
nanosecond, choosing the even integer at a tie. Track-duration references apply
the same exact scale and rounding. A null result marks an expected rejection
outside the signed 64-bit nanosecond range.

The binary64 track scale is stored as sixteen hexadecimal digits, avoiding decimal
serialization differences. The vectors cover signed and unsigned boundaries,
subnormal and maximum finite scales, adjacent values around one, positive and
negative half-nanosecond ties, cancellation of large codec delays, and 256 AAC
frame offsets at 44.1, 48, 7.35 and 96 kHz. Four hundred additional cases use
Python's deterministic random seed 9559. Clock tests and real-file packet-clock
comparisons run in both managed and Native AOT builds.
