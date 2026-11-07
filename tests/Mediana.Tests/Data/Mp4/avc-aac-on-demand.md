# Indexed audiovisual source

`avc-aac-on-demand.mp4` combines the generated 12-second, 64×48, 5 fps
`hls-on-demand.mp4` video with a generated 997 Hz, 48 kHz mono AAC signal.
The video contains reordered pictures and independently decodable access points
every two seconds. The audio has an explicit edit selecting 576,000 PCM samples.

Reproduction with FFmpeg:

```sh
ffmpeg -i hls-on-demand.mp4 \
  -f lavfi -i 'sine=frequency=997:sample_rate=48000:duration=12' \
  -map 0:v:0 -map 1:a:0 -c:v copy -c:a aac -b:a 64000 \
  -movflags +faststart -shortest avc-aac-on-demand.mp4
```

Tests request the final audiovisual second from a cold decoder, verify exact
video/sample counts and timestamp order, bound source reads to the needed region,
and compare repeated independent encoded payloads.
