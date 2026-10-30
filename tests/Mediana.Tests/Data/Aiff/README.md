# AIFF PCM references

These AIFF/AIFF-C files contain generated tones and per-channel impulses from the
matching [WAVE fixtures](../Wave/README.md). Encoding and independent decoding used
FFmpeg 6.1.1. `references.json` records compression formats, stream parameters and
SHA-256 values. Every independently decoded byte equals the original WAVE PCM
reference; the tests reuse those `.pcm` files.

| File stem | Rate | Samples per channel | Encoding | PCM reference |
| --- | --- | --- | --- | --- |
| `s8-mono` | 8000 | 1099 | AIFF signed 8-bit | `../Wave/u8-mono.pcm` |
| `s16-stereo` | 44100 | 6058 | AIFF signed 16-bit big-endian | `../Wave/s16-stereo.pcm` |
| `s24-stereo` | 48000 | 6594 | AIFF signed 24-bit big-endian | `../Wave/s24-stereo.pcm` |
| `s32-stereo` | 48000 | 6594 | AIFF signed 32-bit big-endian | `../Wave/s32-stereo.pcm` |
| `f32-stereo` | 44100 | 6058 | AIFF-C float32 big-endian | `../Wave/f32-stereo.pcm` |
| `f64-stereo` | 48000 | 6594 | AIFF-C float64 big-endian | `../Wave/f64-stereo.pcm` |
| `s16le-stereo` | 44100 | 6058 | AIFF-C `sowt` signed 16-bit little-endian | `../Wave/s16-stereo.pcm` |

The public audio session is checked in managed and Linux Native AOT execution,
with packets of at most 127 samples and a 16 KiB media-buffer budget.
Verification compares every sample, channel order,
PTS, duration, end of stream and retained frame after session disposal. Separate
unit vectors cover integer extrema, non-byte-aligned precision, floating-point bit
patterns, offsets/block fill, truncated inputs, duplicate chunks, invalid extended
sample rates, resource limits and cancellation.

Generate each input from the matching WAVE file, selecting the codec in
`references.json`:

```sh
ffmpeg -hide_banner -v error -nostdin -y -i source.wav -map_metadata -1 -c:a pcm_s16be -f aiff output.aiff
ffmpeg -hide_banner -v error -nostdin -y -i output.aiff -c:a pcm_s16le -f s16le output.pcm
```

Use `pcm_s8`, `pcm_s24be`, `pcm_s32be`, `pcm_f32be`, `pcm_f64be` or `pcm_s16le`
for the other encodings. The matching reference storage is `u8`, `s16le`, `s32le`,
`f32le` or `f64le`. Tests do not invoke an external media process.

Format sources: [Apple AIFF-C specification, COMM/FVER/SSND](https://www.mmsp.ece.mcgill.ca/Documents/AudioFormats/AIFF/Docs/AIFF-C.9.26.91.pdf)
and [FFmpeg's AIFF compression mappings](https://github.com/FFmpeg/FFmpeg/blob/n6.1.1/libavformat/aiff.c).

The supported subset requires `COMM` and AIFF-C `FVER` before `SSND`, mono/stereo,
and a positive integral sample rate. Integer AIFF supports 1–32 bits; AIFF-C
supports `NONE`, 16-bit `sowt`/`twos` and big-endian `fl32`/`fl64`. Compressed
AIFF-C audio, multichannel layouts, tags and cover extraction remain unsupported.
