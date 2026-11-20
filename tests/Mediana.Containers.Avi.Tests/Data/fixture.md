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

## MP3 packetization

`mjpeg-mp3.avi` copies the same twelve JPEG pictures and encodes the mono sine
as 96 kbit/s MPEG-1 Layer III with FFmpeg 6.1.1/libmp3lame. The JSON records
independent packet offsets, sizes and SHA-256 hashes. Tests rebuild audio-only
AVI files with chunks from one byte to the whole audio payload to exercise
headers and frames split across RIFF chunks.

```sh
ffmpeg -v error -i mjpeg-pcm.avi -c:v copy -c:a libmp3lame -b:a 96000 mjpeg-mp3.avi
ffprobe -v error -select_streams a -show_packets -show_data_hash sha256 \
  -of json mjpeg-mp3.avi > mjpeg-mp3.json
```
