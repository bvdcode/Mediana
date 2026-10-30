H.264 multislice references use FFmpeg 6.1.1 with libx264. All three sequences
contain 18 progressive 96 × 80 YUV420 pictures at 6 fps, with I/P/B pictures,
two closed GOPs, three reference pictures and default scaling matrices.
BT.709 primaries, transfer and matrix with limited range are explicitly signalled.
FFmpeg independently decodes each complete sequence to the adjacent raw YUV file.
`MULTISLICE.json` records file lengths and SHA-256 values.

Common generation options:

```sh
ffmpeg -f lavfi -i "testsrc2=size=96x80:rate=6:duration=3" -an \
  -c:v libx264 -pix_fmt yuv420p \
  -color_primaries bt709 -color_trc bt709 -colorspace bt709 -color_range tv \
  -x264-params "$parameters" output.mp4
ffmpeg -i output.mp4 -map 0:v -f rawvideo -pix_fmt yuv420p output.sequence.yuv
```

Common parameters: `bframes=2:b-adapt=0:ref=3:keyint=12:min-keyint=12:scenecut=0`.

| Sequence | Additional parameters | First macroblocks per picture |
| --- | --- | --- |
| cavlc | `threads=1:cabac=0:8x8dct=0:slice-max-mbs=7` | 0, 7, 14, 21, 28 |
| cabac | `threads=1:cabac=1:8x8dct=1:direct=temporal:slice-max-mbs=7` | 0, 7, 14, 21, 28 |
| cabac-sliced | `threads=3:sliced-threads=1:slices=3:cabac=1:8x8dct=1:direct=spatial` | 0, 12, 18 |

All references enable filtering across slice boundaries (`disable_deblocking_filter_idc=0`).
Quantizer deltas vary between slices. The first two sequences deliberately begin
slices inside macroblock rows. These small references verify reconstruction,
reordering and timing; they do not establish sustained throughput.

Normative source: [ITU-T H.264 (08/2024)](https://www.itu.int/rec/T-REC-H.264-202408-S/en),
clauses 6.4.8 (neighbour availability), 7.4.1.2.4 (picture boundaries),
7.4.3.3 (reference marking) and 8.7 (filtering after picture construction).
