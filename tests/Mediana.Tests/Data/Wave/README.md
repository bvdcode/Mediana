# WAVE PCM fixtures

These files contain generated tones and distinct per-channel impulses. Generation
and independent decoding used FFmpeg/ffprobe 6.1.1. Mediana and its tests do not
invoke or require those tools. `references.json` records each generation command,
source/reference SHA-256 and the independent stream parameters.

| File stem | Source samples | Sample rate | Channels | Reference storage |
| --- | --- | --- | --- | --- |
| `u8-mono` | 1099 | 8000 Hz | 1 | `u8` |
| `s16-stereo` | 6058 | 44100 Hz | 2 | `s16le` |
| `s24-stereo` | 6594 | 48000 Hz | 2 | `s32le` |
| `s32-stereo` | 6594 | 48000 Hz | 2 | `s32le` |
| `f32-stereo` | 6058 | 44100 Hz | 2 | `f32le` |
| `f64-stereo` | 6594 | 48000 Hz | 2 | `f64le` |
| `s24-surround` | 6594 | 48000 Hz | 6 | `s32le` |

Each `.pcm` file contains the entire independently decoded sequence, tightly
interleaved and little-endian. Signed 24-bit input expands to left-aligned signed
32-bit output, preserving signal scale. Float samples retain their source precision.
The 8-bit mono file has an odd data length and a RIFF padding byte. All files also
contain metadata chunks; float files include their format/fact declarations, and
multichannel/24-bit files exercise WAVEFORMATEXTENSIBLE channel masks.

Managed and Linux Native AOT checks compare every output sample through the public
audio session in blocks of at most 127 samples per channel, with a 16 KiB media-buffer
budget. They verify stream format, speaker order, exact duration, all PCM PTS,
end of stream and frame ownership after session disposal. Unit vectors additionally
cover integer extrema, signed 24-bit expansion, float bit patterns, valid-precision
padding, unassigned channels, malformed data, resource limits and cancellation.

The reader supports one `data` chunk in little-endian RIFF/WAVE. RF64, RIFX,
compressed WAVE and `LIST/wavl` audio/silence sequences are unsupported. Metadata
chunks are skipped within configured bounds; tags and artwork are not extracted.
Trailing chunks and odd padding are checked when the stream is drained.

Format references: [RIFF chunks](https://learn.microsoft.com/en-us/windows/win32/xaudio2/resource-interchange-file-format--riff-),
[WAVEFORMATEX](https://learn.microsoft.com/en-us/windows/win32/api/mmreg/ns-mmreg-waveformatex),
[WAVEFORMATEXTENSIBLE precision and channels](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ksmedia/ns-ksmedia-waveformatextensible).

To reproduce an independent reference, take the matching source command from
`references.json`, then select the storage in the table:

```sh
ffmpeg -hide_banner -v error -nostdin -y -i <stem>.wav -c:a pcm_<storage> -f <storage> <stem>.pcm
ffprobe -v error -show_entries stream=sample_rate,channels,channel_layout,duration_ts,time_base,codec_name,bits_per_sample,bits_per_raw_sample -of json <stem>.wav
```

| File | SHA-256 |
| --- | --- |
| `f32-stereo.pcm` | `2ef38ef0ca367f7dbe90d327204e64a24cc819c87c6e452d08231794ca5ef034` |
| `f32-stereo.wav` | `86a1b928b87df73c7f83b83c44feb7c7bbeed7f3153f1b9d1d09894f818b8a26` |
| `f64-stereo.pcm` | `7c47c240071d01276c0bf9aee50416d512835a2dbb498c3b4b717421d0a670e7` |
| `f64-stereo.wav` | `0544e50bb9a49d2adec59afdf9e61b92796498667734b74c76e64873ecf1c8c6` |
| `s16-stereo.pcm` | `3c43064a7a7904bd027c9b77d53eb714cc3965c931ed10e64ac51dfa529de55b` |
| `s16-stereo.wav` | `e880c1af44abfb7c4266cea971170194237a9953fe8c0ff8d40b628ebf2b7b08` |
| `s24-stereo.pcm` | `55dd6ff7a222f0fff9727c94ba32dfd5e9b64700811b129dec9a135412197023` |
| `s24-stereo.wav` | `1810208b852d9e863a0e97570753d5bc9b140ecfc3397ca9e449d7a2819d639d` |
| `s24-surround.pcm` | `e5d793eb54f5ece824a71fdf9117d5b2d1af376a9e611656d31045e9bee3318f` |
| `s24-surround.wav` | `347c66b271d6861fdc26a07fd4246d13b317a289443ac7bd5a9f2344656adf75` |
| `s32-stereo.pcm` | `90a1c43c989a69acbe88cd3ae4a5d6f08d4dc9ee74e43b7c31b8351f9ca3aa4c` |
| `s32-stereo.wav` | `2cee2a99e5916a72bf6f8e3cbacf9a0d48d1de1b85b3efc2e81e856aec2d6e7b` |
| `u8-mono.pcm` | `ff442a0f1fba39b34ff9c251ca095655a01f73d2872481acbd49c12693a623e1` |
| `u8-mono.wav` | `4a594ddd33da8bfcef06555bea32ddfd68b0e553b4d7936cead647936a0d6f32` |
