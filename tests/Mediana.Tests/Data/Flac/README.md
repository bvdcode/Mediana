# Native FLAC container references

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
These checks do not claim FLAC PCM decoding, audio frame CRC/MD5 verification,
seeking, cuesheet interpretation or image codec validation.

Format: [RFC 9639](https://www.rfc-editor.org/rfc/rfc9639.html). The reader is an
original C# implementation. FFmpeg/ffprobe generate independent fixture evidence;
the library and tests do not execute them.
