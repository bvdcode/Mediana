# Timed container previews

The three inputs contain 35 synthetic 64×48 frames at 25 fps. Every reference
contains the 26th displayed 8-bit YUV 4:2:0 frame, at one second elapsed.
The MPEG files carry a nonzero starting presentation timestamp and use B pictures.
The ASF preview reads sequentially because its container reader has no index.

```sh
ffmpeg -f lavfi -i testsrc2=size=64x48:rate=25:duration=1.4 -an \
  -c:v mpeg2video -threads 1 -bf 2 -g 12 -q:v 3 -f mpeg timed-mpeg2.mpg
ffmpeg -f lavfi -i testsrc2=size=64x48:rate=25:duration=1.4 -an \
  -c:v libx264 -pix_fmt yuv420p -threads 1 -bf 2 -g 25 -f mpegts timed-annexb.ts
ffmpeg -f lavfi -i testsrc2=size=64x48:rate=25:duration=1.4 -an \
  -c:v wmv2 -threads 1 -g 25 -q:v 3 -f asf timed-linear.wmv
ffmpeg -i timed-mpeg2.mpg -ss 1 -frames:v 1 -an -pix_fmt yuv420p -f rawvideo timed-mpeg2.yuv
ffmpeg -i timed-annexb.ts -ss 1 -frames:v 1 -an -pix_fmt yuv420p -f rawvideo timed-annexb.yuv
ffmpeg -i timed-linear.wmv -ss 1 -frames:v 1 -an -pix_fmt yuv420p -f rawvideo timed-linear.yuv
```
