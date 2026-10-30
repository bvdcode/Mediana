# MP3 in MP4 references

`avc-mp3-44.mp4` and `avc-mp3-22.mp4` combine the existing closed-GOP
`avc-segments.mp4` video with independently encoded generated tones. FFmpeg 6.1.1
copies the video and encodes MPEG-1 Layer III at 44100 Hz stereo /128 kbit/s or
MPEG-2 Layer III at 22050 Hz mono /64 kbit/s. Each file contains about 7.2 seconds.

The `.mp4.json` files contain independent compressed packet SHA-256 values,
sizes, durations, PTS/DTS and stream parameters in the original media timeline.
The audio `mp4a` entries use ES descriptor object type indications `0x6B` and
`0x69` respectively. MP3 has no AudioSpecificConfig; its first four frame-header
bytes describe the coded sample rate, version and channels.

Generation:

```sh
ffmpeg -hide_banner -v error -nostdin -y -i avc-segments.mp4 -f lavfi -i sine=frequency=317:sample_rate=44100:duration=7.2 -map 0:v:0 -map 1:a:0 -c:v copy -c:a libmp3lame -b:a 128k -ac 2 -map_metadata -1 avc-mp3-44.mp4
ffmpeg -hide_banner -v error -nostdin -y -i avc-segments.mp4 -f lavfi -i sine=frequency=631:sample_rate=22050:duration=7.2 -map 0:v:0 -map 1:a:0 -c:v copy -c:a libmp3lame -b:a 64k -ac 1 -map_metadata -1 avc-mp3-22.mp4
ffprobe -v error -ignore_editlist 1 -show_packets -show_data_hash sha256 -show_entries stream=index,codec_name,time_base,sample_rate,channels:packet=stream_index,pts,dts,duration,size,data_hash -of json input.mp4
```

Format sources: [MP4RA object type assignments](https://mp4ra.org/registered-types/object-types)
and [ITU-T H.222.0 (10/2014), table 2-34](https://www.itu.int/rec/T-REC-H.222.0-201410-S/en).
MPEG-1/2 audio uses MPEG-TS stream types `0x03`/`0x04`. MPEG-2.5 transport is
outside the supported subset. Layer I/II, free-format framing and changing audio
formats are unsupported.

MP3 frames may depend on an earlier bit reservoir, and synthesis has history.
An AVC IDR segment boundary does not make its MP3 audio independently decodable.
Compressed stream copy retains that dependency and does not declare
`EXT-X-INDEPENDENT-SEGMENTS` or insert audio preroll.
