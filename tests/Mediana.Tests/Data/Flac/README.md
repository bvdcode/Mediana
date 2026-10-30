# Native FLAC references

These native FLAC files verify STREAMINFO, Vorbis comments, original cover bytes
and the complete compressed audio extent. `references.json` records the original
source hashes, encoder/probe commands, independent ffprobe metadata and packet
positions/hashes. The concatenated audio-byte hash begins at ffprobe's first audio
packet position and covers every audio packet through EOF.

| File | Input | Rate/channels/precision | Samples per channel | Pictures |
| --- | --- | --- | ---: | --- |
| `stereo-covers.flac` | Original synthetic harmonic signal | 48000/2/16 | 275040 | Original PNG and JPEG |
| `mono-speech.flac` | Recorded VOiCES source | 16000/1/16 | 54400 | None |

The stereo source is `../Waveform/music.wav`; the covers are
`../Metadata/cover.png` and `../Metadata/cover.jpg`. These original generated
signals/artwork and their derived FLAC are under the repository's MIT license.

`mono-speech.flac` is a lossless FLAC encoding of
`Lab41-SRI-VOiCES-src-sp0307-ch127535-sg0042.wav`, from *Voices Obscured in Complex
Environmental Settings (VOiCES)* by Lab41, In-Q-Tel and SRI International,
accessed 2026-10-28 through the
[PyTorch tutorial asset](https://download.pytorch.org/torchaudio/tutorial-assets/Lab41-SRI-VOiCES-src-sp0307-ch127535-sg0042.wav).
It adds a title tag and changes the container/encoding from WAVE to FLAC.
The speech source and this derived FLAC are licensed under
[Creative Commons Attribution 4.0 International](https://creativecommons.org/licenses/by/4.0/).
See the [dataset documentation](https://iqtlabs.github.io/voices/Lab41-SRI-VOiCES_README/),
[registry license declaration](https://registry.opendata.aws/lab41-sri-voices/)
and [WAVE source attribution](../Waveform/README.md).

Shared managed/Native AOT checks verify the real metadata, every compressed byte,
exact picture contents and retained picture/packet lifetime after reader disposal.
Managed packet limits are 127 and 16384 bytes; Native AOT uses 127. Media-buffer
budget is 128 KiB. The reader borrows its source and all final leases release.
The container returns arbitrary compressed chunks without frame timestamps.
These container-only checks do not claim PCM decoding, audio frame CRC/MD5 verification,
seeking, cuesheet interpretation or image codec validation.

Format: [RFC 9639](https://www.rfc-editor.org/rfc/rfc9639.html). The reader is an
original C# implementation. FFmpeg/ffprobe generate independent fixture evidence;
the library and tests do not execute them.

## Lossless PCM and waveform references

`decode-references.json` records independent signed32 little-endian PCM hashes,
complete sample counts, source hashes and exact encode/decode commands. Shared
managed/Native AOT verification hashes every decoded PCM byte, checks continuous
sample timestamps, repeated EOF, borrowed source position and retained frames.
The decoder media-buffer budget is 512 KiB; all buffers release after the final
retained frame. Managed decoding includes one-byte chunks; Native AOT uses 127.
No test invokes an external media process.

| File | Rate/channels/precision | Samples/channel | Origin |
| --- | --- | ---: | --- |
| `stereo24.flac` | 44117/2/24 | 9973 | Original generated harmonic signal with full-scale events |
| `surround24.flac` | 48000/6/24 | 6533 | Original generated 5.1 signal with full-scale events |
| `stereo32.flac` | 48000/2/32 | 79 | Prescribed mid-side verbatim vector, two frames and a short final block |

The generated 24-bit signals are MIT licensed. For each sample `i` and channel
`c`, with `limit = 2^(bits-1)`, their integer value is
`int((limit-1) * 0.7 * sin((i*(c+1)+c*53)*0.013))`. Every 997th sample is replaced
by `-limit` or `limit-1`, alternating by `(i//997+c)%2`. Values are left-aligned in
signed32 little-endian before the manifest's encoding commands. The 44117 Hz file
also checks an uncommon sample rate. The 5.1 file explicitly declares the standard
speaker mask `0x3f`.

The installed FFmpeg 6.1.1 FLAC encoder reduces 32-bit input to 24 bits, so it is
not used to construct the 32-bit vector. The MIT-licensed vector contains full-range
32-bit endpoints and odd/negative mid-side values, requiring 33-bit side samples.
The independent FFmpeg decoder reproduces all 158 original interleaved values
exactly.

For `stereo-covers` and `mono-speech`, every 400 Hz mono PCM16 RMS/peak window,
final partial count and timestamp matches processing the original WAVE source
exactly, using explicit gains, cutoff `.97`, Reflect boundaries and 37-sample
windows. Respective complete output counts are 2292 and 1360 samples.
Frame CRC8/CRC16 is checked by the decoder. STREAMINFO MD5 verification, seeking,
encoding and custom channel layouts remain open. Speech attribution and the
CC BY 4.0 license above continue to apply to the speech fixture.
