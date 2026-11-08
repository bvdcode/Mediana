# AC-3 MP4 inputs

The MP4 files contain the unchanged syncframes from the original mathematical signals in `Mediana.Codecs.Ac3.Tests/Data`.
They were independently multiplexed with FFmpeg 6.1.1:

```sh
ffmpeg -nostdin -hide_banner -loglevel error -i mono.ac3 -map 0:a:0 -c copy -f mp4 mono.mp4
ffmpeg -nostdin -hide_banner -loglevel error -i stereo.eac3 -map 0:a:0 -c copy -f mp4 stereo-eac3.mp4
```

Each file contains four 1536-sample access units at 48 kHz. The tests verify specific-box metadata, exact packet/sample clocks, complete PCM equality with the original elementary streams, seeking and retained ownership. The AC-3 specific box has a three-byte payload; the E-AC-3 specific box has a six-byte payload, including trailing reserved data, and declares one independent stereo substream.

`one-block-eac3.mp4` contains twelve repetitions of the authored `enhanced-one.eac3` syncframe, multiplexed with the same command. Its sample table groups six one-block syncframes into each of two 1536-sample access units. The tests verify bundle decoding and compare the first output after indexed preroll with continuous PCM, both for MP4 bundles and individual elementary syncframes.

`transient.mp4` is a stream-copy remux of the original ten-frame `transient.eac3` mathematical signal in the dedicated codec corpus. It preserves all 15,360 mono samples at 48 kHz. It checks delayed transient reconstruction through raw and MP4 sessions, indexed seeking, HLS PCM preparation and complete AAC encoding, including priming and padding counts.
