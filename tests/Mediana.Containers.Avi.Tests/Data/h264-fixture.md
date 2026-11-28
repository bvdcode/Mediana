# H.264 in AVI fixtures

Both files contain 48 frames at 12 fps, with four closed GOPs and B pictures.
`h264-annexb.avi` carries SPS/PPS in-band and has no extra data in the bitmap
header. `h264-avcc.avi` carries SPS/PPS in an AVC configuration record and
uses four-byte NAL lengths. FFmpeg decodes both to identical display-order YUV.

```sh
filter="setsar=1,drawbox=x=0:y=0:w=64:h=12:color=white:t=fill:enable='gte(t,1)*lt(t,2)',drawbox=x=0:y=0:w=64:h=12:color=black:t=fill:enable='gte(t,2)*lt(t,3)'"
encode() {
  ffmpeg -f lavfi -i testsrc2=size=64x48:rate=12:duration=4 -vf "$filter" \
    -an -c:v libx264 -pix_fmt yuv420p -threads 1 -bf 2 -g 12 \
    -x264-params scenecut=0:open-gop=0 -color_primaries bt709 \
    -color_trc bt709 -colorspace bt709 "$@"
}
encode -vtag H264 -f avi h264-annexb.avi
encode -f mp4 h264.mp4
ffmpeg -i h264.mp4 -map 0:v -c:v copy \
  -bsf:v 'setts=pts=N:dts=N:duration=1:time_base=1/12' -vtag avc1 -f avi h264-avcc.avi
ffmpeg -i h264-annexb.avi -map 0:v:0 -fps_mode passthrough \
  -pix_fmt yuv420p -f rawvideo - | gzip -n > h264-display.yuv.gz
```
