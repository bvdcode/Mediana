# Video preview color fixture

`untagged-avc.mp4` contains a generated 96x64 test pattern, ten progressive AVC
frames at 10 fps, square pixels and no audio. The SPS leaves color primaries,
transfer characteristics and matrix coefficients unspecified. Range and chroma
position follow the H.264 syntax defaults. The file contains no `colr` box.

The fixture was created with FFmpeg 6.1.1:

```sh
ffmpeg -hide_banner -loglevel error -f lavfi -i testsrc2=size=96x64:rate=10:duration=1 -an -c:v libx264 -profile:v baseline -pix_fmt yuv420p -x264-params threads=1 -y untagged-avc.mp4
```

SHA-256: `18a55e5d2defde028dbd69bec29e32c68ca513432de3d596704d51734c791344`.

CLI tests verify both preview routes produce complete sRGB PNGs. The inferred
SD profile produces the same bytes as an explicit SMPTE 170M / BT.709 transfer /
BT.601 matrix interpretation, while the original frame metadata remains unknown.
Tests run without FFmpeg. Color conversion itself has separate independent pixel
references in the MP4 corpus.

## Container color descriptions

Five stream-copy variants preserve every coded access unit and every decoded
YUV sample of their source. The first four named `709`/`2020` use
`untagged-avc.mp4`; the conflict case uses `../Mp4/avc-weighted.mp4`, whose
SPS declares BT.709 limited range.

| File | Box type | Primaries / transfer / matrix | Range declaration |
| --- | --- | --- | --- |
| `container-709-limited.mp4` | nclx | 1 / 1 / 1 | limited |
| `container-709-full.mp4` | nclx | 1 / 1 / 1 | full |
| `container-709.mov` | nclc | 1 / 1 / 1 | absent |
| `container-601-conflict.mp4` | nclx | 6 / 1 / 6 | full |
| `container-2020-pq.mp4` | nclx | 9 / 16 / 9 | limited |

Creation uses stream copy and explicit container fields:

```sh
ffmpeg -i untagged-avc.mp4 -map 0:v:0 -c copy -map_metadata -1 -movflags +write_colr -color_primaries bt709 -color_trc bt709 -colorspace bt709 -color_range tv container-709-limited.mp4
```

Use `pc` for the full-range variant and `.mov` for nclc. The HDR variant
uses `bt2020`, `smpte2084`, `bt2020nc` and `tv`. The conflict variant uses
`smpte170m`, `bt709`, `smpte170m` and `pc` with the tagged source.
The HDR labels test rejection of an unsupported preview transform; the
underlying samples are the same generated SDR pattern.

The Annex-B elementary-stream SHA-256 for all variants of the untagged source
is `7838a08f745bf792c9e9c300c3b1955d21ad8fb0cb1aaffc88c128894285f4cc`;
the decoded native-range YUV sequence SHA-256 is
`e0d88b5d9d593c8cbeb552764095351968c75c9296939d496ab16cbbacd0ce11`.
For the conflict input these hashes are
`317ccd0d54d4f9a77aee6967deeb8b1467d5683aa204e1b10da763f2f707aab8` and
`5f42c51a79b91870aff4ab2e7af6fe1c9a98d74bc71a101e8b353ed2ec8781d1`.
No pixel-format conversion is requested when checking native YUV samples;
forcing limited-range YUV output would change full-range samples.

Four `*.frame0.srgb.rgba` files contain complete independent zimg sRGB references
for the first picture. They retain source storage dimensions, with bilinear chroma
reconstruction, no dithering and the same ideal BT.1886 display used by the
other SDR references. Input color parameters are explicit so the conflict case
tests container precedence even when an external decoder prefers SPS labels.
Every RGBA channel is checked within one 8-bit code.

```sh
ffmpeg -i container-709-limited.mp4 -vf "zscale=matrixin=709:transferin=709:primariesin=709:rangein=limited:chromalin=left:matrix=gbr:transfer=iec61966-2-1:primaries=709:range=full:filter=bilinear:dither=none:agamma=0,format=gbrp,format=rgba" -frames:v 1 -f rawvideo container-709-limited.frame0.srgb.rgba
```

Use full range for `container-709-full`; nclc uses limited range here.
The conflict reference uses `matrixin=6:primariesin=6:rangein=full`.

| Reference | SHA-256 |
| --- | --- |
| `container-709-limited.frame0.srgb.rgba` | `6110b2cf9a1447adf712a50e117fb52a021a00709f7d52af0ea86301171c5b71` |
| `container-709-full.frame0.srgb.rgba` | `0fe0069ff86375a3bfa247dc01b66e49d028681d295d2f7607f4c2f9a58cce62` |
| `container-709.frame0.srgb.rgba` | `6110b2cf9a1447adf712a50e117fb52a021a00709f7d52af0ea86301171c5b71` |
| `container-601-conflict.frame0.srgb.rgba` | `4e04dc71186bb4cc8e676bbb3ebbdedfe5440028ef06114baed583928e588e86` |

`container-segments.mp4` retains all compressed AVC/AAC samples from
`../Mp4/avc-segments.mp4`, adding original SPS/PPS NALs before its eight IDRs
and an `nclx` description of 6/1/6, full range. It contains 36 pictures at 5 fps
and 339 AAC packets. HLS selects three windows of 15, 15 and 6 pictures.
Remux and out-of-order HLS tests require source colors in all 16 output SPS
occurrences (eight configuration injections and eight in-band replacements),
unchanged compressed picture/auxiliary data, exact shifted PTS/DTS and no
retained media buffers.
The fixture SHA-256 is
`355e02b855be0648692973f31b6ea1917457e5c731df233a729091b5b86d21ba`;
its complete native YUV SHA-256 is
`e3980ab6f6fbbd680537e00d31a6960d2fe84d8f73efa8d508257d16883f59dc`.

```sh
ffmpeg -i ../Mp4/avc-segments.mp4 -map 0 -c copy -bsf:v h264_mp4toannexb -map_metadata -1 -movflags +write_colr -color_primaries smpte170m -color_trc bt709 -colorspace smpte170m -color_range pc container-segments.mp4
```
