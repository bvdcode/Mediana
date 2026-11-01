# HD decoding, previews and stream-copy verification

The complete original 720p release of Big Buck Bunny was checked through the
public container, video-session, MPEG-TS and HLS operations. The source contains
1280x720 AVC Main 3.1 at 24 fps, 48 kHz AAC-LC with six channels, and a QuickTime
timecode track. Its declared duration is 596.461667 seconds.

Source: [Blender Foundation release](https://peach.blender.org/download/),
[original MOV mirror](https://www.peach.themazzone.com/big_buck_bunny_720p_h264.mov).
The film is © 2008 Blender Foundation / www.bigbuckbunny.org, released under
[CC BY 3.0](https://peach.blender.org/about/).
The 416,751,190-byte input has SHA256
`45c8bafeb9a53df7f491198d2e71529701bcf1cd51805782089fac1d32869f9b`.

## Complete results

- Native AOT video decoding returned all 14,315 pictures. Every visible YUV byte,
  PTS and frame duration matched independent FFmpeg frame hashes. The comparison
  covered 19,789,056,000 decoded bytes and reached EOF with no retained media storage.
- Video-only MPEG-TS copying preserved every decoded picture and duration. Every
  output PTS was exactly one second after its source PTS.
- Default A/V copying produced 42,275 packets. The Native AOT and managed TS files
  were identical. Their independently extracted ADTS audio matched all 27,960
  original AAC packets, including framing and channel declarations.
- HLS copying produced 100 TS segments and a finalized VOD playlist. Every video
  picture, PTS and duration matched independently; every AAC packet matched the
  original stream. All 101 Native AOT and managed files were identical.
- The playlist duration was 596.48 seconds, including the final coded audio tail.
  The six-channel audio result establishes stream preservation during copying.

QuickTime signed composition offsets use their declared format semantics.
Linear-movie timestamps advance the decode schedule by the complete table's
required lead, preserve presentation times, and merge tracks by adjusted DTS.
Default CLI copying selects audio and video; timecode remains available to probing
and explicit stream selection. See [module contracts](architecture.md).

## AAC 5.1 decoding

The Native AOT audio session decoded all 27,960 original AAC-LC access units
into 28,631,040 Float32 samples per channel, or 687,144,960 bytes across the six
speakers. Coded encoder delay and final padding remain included. Output order is
FL, FR, FC, LFE, BL, BR; the operation reached EOF with zero live media-buffer
bytes. The PCM SHA256 is
`3b84ad3f2b736de877fa993db10776332cbad47c153622c3786ceab6e8637802`.

FAAD2 2.11.1 independently decoded the complete original ADTS audio extracted
during stream-copy verification. Comparing all 171,786,240 scalar samples found
a maximum absolute difference of `5.662441253662109e-7`; every value was finite.
Per-channel RMS errors ranged from `1.43e-9` to `1.15e-8`. The comparison uses
the unchanged deterministic `2e-6` tolerance.

FFmpeg 6.1.1 produced the same sample extent but differed in 513 front-right
blocks, with maximum error `0.3445736765861511`; the other five speakers matched
within `6.86e-7`. Those blocks coincide with additional window transitions
explicitly permitted by ISO/IEC 14496-3:2001, section 4.6.11.3.1. Mediana's
windowed overlap-add result matches FAAD2 throughout the complete track.
A separate authored stereo reference covers all 16 sequence transitions, all
four previous/current sine/KBD shape combinations in both channels, and switching
from independent to common windows. Every sample matches FAAD2 in managed and
Native AOT execution. See the [AAC references](../tests/Mediana.Tests/Data/AacMultichannel/README.md).

## PNG preview

Requesting a preview at 298.231 seconds selected the picture at 298.250 seconds
(PTS 715800 at 1/2400). The result is a 1280x720 square-pixel sRGB PNG.
The original QuickTime file has neither a `pasp` declaration nor an SPS sample
aspect ratio. Its format-defined square-pixel geometry reaches the decoder
through the shared source-description contract.

Native AOT and managed builds produced identical PNG files. Independent decoding
selected the same picture and matched its YUV SHA256. Comparing all 3,686,400
RGBA values against FFmpeg/zimg with explicit BT.709, limited range, top-left
chroma and sRGB conversion found a maximum difference of one 8-bit code and a
mean absolute difference of 0.001181. PNG structure, CRCs, sRGB and square-pixel
declarations were checked; the preview operation released all media storage.

The complete TS/HLS comparisons were repeated after carrying this geometry into
SPS parameters. Both outputs declare SAR 1:1; all picture hashes, presentation
times, frame durations and original AAC packets still match the source.

## CPU and memory

Linux x64, Intel Core i5-12450H, .NET 10.0.13 Native AOT and FFmpeg
6.1.1-3ubuntu5. Each process ran sequentially with affinity to the same logical
CPU; FFmpeg decoding used one thread. One full-source run was recorded per
implementation and revision on a shared host. CPU time is user plus system time;
memory is process peak RSS.

The complete decode comparison was repeated before and after reusing six-tap
luma filter sums across prediction blocks. Each Native AOT consumer decoded the
original file through EOF, hashed every picture, checked every reference hash and
exact time, and released all media storage. The complete CSV outputs were
identical. A fresh FFmpeg decode matched every original reference frame.

| Decode and hash all 14,315 pictures | CPU | Elapsed | Peak RSS |
| --- | ---: | ---: | ---: |
| Mediana before (`e1f5fea`) | 711.74 s | 731.66 s | 106.8 MiB |
| Mediana after (`58f9da8`) | 466.67 s | 481.56 s | 106.9 MiB |
| FFmpeg | 90.27 s | 92.01 s | 74.5 MiB |

The observed CPU reduction is 34.43%, a 1.53-fold speedup. The complete optimized
operation still uses 5.17 times FFmpeg's CPU. This measurement includes SHA256 and
per-picture text output; the Native consumer also checks reference hashes and
exact times while decoding.

Luma interpolation extends picture boundaries once per tile and reuses unrounded
horizontal sums for diagonal samples. All quarter-sample phases retain their
specified rounding and clipping. Managed and Native AOT tests check 4,752 block,
phase, pattern and coordinate combinations against scalar convolution, including
picture borders, extreme coordinates and blocks crossing tile boundaries.

The complete copying results at revision `5d6e272` are:

| Complete operation | Mediana CPU | FFmpeg CPU | Mediana peak RSS | FFmpeg peak RSS |
| --- | ---: | ---: | ---: | ---: |
| Copy video and audio to MPEG-TS | 0.863 s | 0.857 s | 30.2 MiB | 57.5 MiB |
| Copy video and audio to VOD HLS | 2.352 s | 0.903 s | 28.8 MiB | 78.0 MiB |

FFmpeg and Mediana use their respective transport muxing policies.
A separate [full-resolution re-encoding comparison](h264-hd-encoding.md)
measures the complete film's CPU, peak RSS and source error against libx264.
Broader HD source acceptance, perceptual quality and full-resolution audiovisual
HLS encoding remain open.

The complete original six-channel AAC track was also decoded to raw Float32 PCM
in separate processes on logical CPU 2. FFmpeg used one decoder and output
thread, with container edit-list application disabled to preserve the complete
coded sample extent. Hashing and sample comparison ran outside the measurements.

| Decode all 27,960 AAC access units | CPU | Elapsed | Peak RSS |
| --- | ---: | ---: | ---: |
| Mediana | 5.353 s | 5.802 s | 27.7 MiB |
| FFmpeg 6.1.1 | 1.375 s | 1.376 s | 55.2 MiB |

Mediana used 3.89 times the CPU and about half the peak RSS on this input. These
are single full-file observations; the front-right sample differences described
above also apply to the measured outputs. The FAAD2 comparison establishes
complete-sample correctness separately from this performance comparison.
