# Interleaved AVI reference

The MJPEG source uses the mathematical test pattern documented in the MJPEG
corpus. Audio is a generated 997 Hz sine, 48000 Hz mono, 1.5 seconds. Twelve
64×48 JPEG pictures at eight frames per second and 72000 PCM16 samples are
interleaved into one RIFF/AVI file.

```sh
ffmpeg -v error -i mjpeg-420.mkv \
  -f lavfi -i sine=frequency=997:sample_rate=48000:duration=1.5 \
  -map 0:v -map 1:a -r 8 -c:v copy -c:a pcm_s16le mjpeg-pcm.avi
ffprobe -v error -show_streams -show_packets -of json mjpeg-pcm.avi > mjpeg-pcm.json
```

The explicit output rate prevents time-base expansion and empty duplicate
chunks during stream copy. Separate structural references exercise empty chunks.
