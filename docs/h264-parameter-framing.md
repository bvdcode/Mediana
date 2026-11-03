H.264 parameter parsing distinguishes NAL payload from byte-stream framing.
The decoder validates the declared PPS payload through its RBSP ending; bytes
outside that ending are rejected.

## Byte-stream and length-prefixed boundaries

[ITU-T H.264 (06/2026)](https://www.itu.int/rec/T-REC-H.264-202606-I),
section 7.4.1, requires the last NAL byte to be nonzero. Annex B.1 and B.2 place
start-code prefixes, their additional zero byte, and leading or trailing zero
bytes outside the NAL. A PPS ends with RBSP trailing bits; CABAC zero words
belong to the separate coded-slice syntax.

Consequently, a zero adjoining an Annex B start code cannot simply become
part of a length-prefixed PPS. This is a framing distinction rather than an
additional PPS field.

## Observed TS-to-MP4 conversion

The complete-sequence [reference-filter control](h264-reference-filter.md)
retains two independently verified MPEG-TS outputs. Direct inspection of the
opening video PES and MP4 AVC configuration gives:

| Parameter | Original MPEG-TS NAL | FFmpeg 6.1.1 remuxed MP4 configuration NAL |
| --- | --- | --- |
| PPS | `68ce3c80` | `68ce3c8000` |

The SPS is identical. Both original PPS units have valid endings and four-byte
following start codes. The extra zero is inside the MP4 configuration's
declared PPS length. The validated Native AOT CLI rejects both remuxed inputs
with an RBSP-ending error and creates no image. The original inputs remain
unchanged.

The strict parser remains unchanged. Independent picture verification of the
original encoded streams does not establish acceptance of these differently
framed MP4 configurations.
