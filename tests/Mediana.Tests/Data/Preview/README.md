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
