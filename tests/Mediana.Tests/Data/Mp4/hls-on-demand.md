# On-demand encoding reference

`hls-on-demand.mp4` contains a generated 64x48 pattern, 60 High-profile AVC pictures
at five frames per second, B-picture reordering, two-second closed GOPs, square
samples and limited BT.709 color. The on-demand encoder selects twelve one-second
windows, including windows starting between source IDRs. Each window has fresh
Baseline encoder state, QP 26 and an independently decodable first picture.

`hls-on-demand.encoded-segments.yuv` contains all 60 output pictures independently
decoded by FFmpeg, tightly packed as Y/Cb/Cr 4:2:0, 4,608 bytes per picture. Tests
request segments in reverse order and repeat the final segment. Every decoded
sample and presentation time must match the reference.

| File | SHA-256 |
| --- | --- |
| hls-on-demand.mp4 | 7aea1cc0dc7b88aa64cf0e94c92d79f10ca73178486f4261d045f7639f8c099a |
| hls-on-demand.encoded-segments.yuv | e48e9db8a22a79c961fe9c55a1b14f2637cedb607fb8974a2bf2baf9bcc17c57 |

Source generation:

```sh
ffmpeg -hide_banner -loglevel error -f lavfi -i testsrc2=size=64x48:rate=5 -t 12 -c:v libx264 -threads 1 -pix_fmt yuv420p -profile:v high -bf 2 -g 10 -keyint_min 10 -sc_threshold 0 -x264-params b-adapt=0:open-gop=0 -color_primaries bt709 -color_trc bt709 -colorspace bt709 -color_range tv -movflags +faststart hls-on-demand.mp4
```

Reference decoding of the generated fMP4 playlist:

```sh
ffmpeg -hide_banner -loglevel error -i index.m3u8 -map 0:v:0 -pix_fmt yuv420p -fps_mode passthrough -f rawvideo hls-on-demand.encoded-segments.yuv
```

FFmpeg prepares the references and is not required to build or run these tests.
