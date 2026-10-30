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
