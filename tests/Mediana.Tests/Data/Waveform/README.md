# Waveform references

These WAVE inputs exercise streaming mono 400 Hz PCM16 and RMS/peak processing.
`references.json` records input/reference SHA-256 values, sample counts, the
independent FFmpeg version and the exact reference commands. Tests consume the
checked-in reference bytes; they do not execute FFmpeg.

| Input | Source | Rate/channels | Source samples per channel | Mono 400 Hz samples |
| --- | --- | --- | --- | --- |
| `silence.wav` | Generated digital silence | 48000/1 | 152160 | 1268 |
| `impulses.wav` | Generated positive impulses, including both endpoints | 44100/2 | 91728 | 832 |
| `music.wav` | Generated harmonic progression with an amplitude envelope | 48000/2 | 275040 | 2292 |
| `speech.wav` | Recorded VOiCES speech | 16000/1 | 54400 | 1360 |
| `fractional.wav` | First 48049 samples per channel of `music.wav` | 48000/2 | 48049 | 401 |

The musical signal is synthetic. Silence, impulses, musical signal, fractional
input and their derived references are provided under the repository's MIT
license. The recorded speech and its derived reference have the separate license
and attribution below.

## Recorded speech attribution

`speech.wav` is an unmodified copy of
`Lab41-SRI-VOiCES-src-sp0307-ch127535-sg0042.wav`, from *Voices Obscured in Complex
Environmental Settings (VOiCES)* by Lab41, In-Q-Tel and SRI International, accessed
2026-10-28. The source file is distributed through the
[PyTorch tutorial assets](https://download.pytorch.org/torchaudio/tutorial-assets/Lab41-SRI-VOiCES-src-sp0307-ch127535-sg0042.wav).
`speech.s16le` is a derived version resampled to mono 400 Hz signed PCM16.

Both speech files are licensed under
[Creative Commons Attribution 4.0 International](https://creativecommons.org/licenses/by/4.0/).
See the [VOiCES dataset documentation](https://iqtlabs.github.io/voices/Lab41-SRI-VOiCES_README/)
and [dataset registry entry](https://registry.opendata.aws/lab41-sri-voices/) for
the dataset description and license declaration.

## Processing and acceptance bounds

The tested profile explicitly selects `CutoffRatio = 0.97` and
`BoundaryMode = Reflect`. Mono gain is 1; stereo gains are 0.5/0.5. Output is
quantized to PCM16 before accumulating 40-sample (100 ms) RMS/peak windows.
The default resampler profile remains separate from this explicit profile.

Every output sample, exact count, PTS/duration and final partial level window is
checked. Managed tests use input packet sizes 127 and 1024; Native AOT uses 127.
The fractional input checks a non-integral resampled duration and its final sample.
Decoder and processing media-buffer budgets are 16 KiB and 256 KiB respectively,
with no retained media buffers after completion.
The same independent level windows also verify the public `AudioWaveformReader`,
with output frames of seven samples to exercise windows spanning frame boundaries.

For normalized PCM, maximum absolute sample error must be at most 0.002 and whole
signal error RMS at most `1/32768 + 0.01 * reference RMS`. For each level window,
absolute RMS error must be at most `0.0002 + 0.01 * reference RMS` and absolute
peak error at most 0.002. These bounds describe this corpus and profile; they do
not assert bit-identical resampling or general codec/rendering equivalence.
