Area downscaling references use exact rational integration of piecewise-constant
source samples. Input/output files contain tightly packed Y, U and V planes,
with 8-bit 4:2:0 samples and no header. `AREA.json` records byte counts and SHA-256.

For one plane, let `r = sourceSize / outputSize`, `j` be the output sample index,
`s` its subsampling factor and `o` the chroma offset in luma samples. The source
sample coordinate is `j*r + (o + 1/2)*(r - 1)/s`. For luma, `s = 1` and `o = 0`.
For chroma, `s = 2`; offsets follow ITU-T H.264 V15 (08/2024), Figure E-1:

| Location | Horizontal offset | Vertical offset |
| --- | --- | --- |
| Left | 0 | 1/2 |
| Center | 1/2 | 1/2 |
| TopLeft | 0 | 0 |
| Top | 1/2 | 0 |
| BottomLeft | 0 | 1 |
| Bottom | 1/2 | 1 |

Each sample occupies a unit cell centered at its integer coordinate. The first
and last cells extend to the image boundaries and beyond. Integrate the source
over a rectangle of width `rx` and height `ry` centered at the mapped position,
divide by its area, then round once to the nearest integer with half upward.
The reference generator uses Python `fractions.Fraction`; it does not call the
production resizer or round intermediate horizontal results.

The synthetic 10x8 input uses `(23*x + 31*y + 57*plane) mod 256`, with local
coordinates in each plane. Six 6x4 outputs cover every chroma location. Complete
96x80 and 94x62 video sequences in `../Mp4` originate from the existing independent
H.264 decode references and scale to 48x40 and 48x30. Their SAR values change from
1:1 to 1:1 and from 4:3 to 235:186 respectively, preserving exact display aspect.

Managed and Native AOT tests compare every visible sample, frame count, timing,
color and SAR. Separate checks cover padded row strides, edge cells, an averaging
checkerboard, even fitting, cancellation, allocation failure and retained output.
This is a stored-sample area filter, with no linear-light conversion, rotation,
upsampling, color conversion or HDR tone mapping.

`TRANSCODING.json` additionally records every independently decoded picture from
area downscaling followed by QP26 AVC encoding. Generate a TS reference using:

```sh
dotnet run --project tools/Mediana.Cli -c Release -- \
  -i "tests/Mediana.Tests/Data/Mp4/$NAME.mp4" "artifacts/$NAME-scaled.ts" \
  --video-codec H264 --video-only --width 48 --qp 26
ffmpeg -v error -xerror -threads 1 -i "artifacts/$NAME-scaled.ts" \
  -map 0:v:0 -fps_mode passthrough -pix_fmt yuv420p -f rawvideo \
  "tests/Mediana.Tests/Data/Mp4/$NAME.scaled-48x$HEIGHT.encoded-qp26.yuv"
```

Use `avc-multislice-cabac`/40 and `avc-weighted`/30 for `NAME`/`HEIGHT`.
The first TS is also retained for CLI integration checks: `--width 49` and
`--height 41` must both select 48x40 and emit the same complete stream. The other
sequence verifies non-square pixels and SAR 235:186 through the encoded SPS.
