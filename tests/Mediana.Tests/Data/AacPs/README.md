# Parametric stereo references

`tonal.aac` and `transients.aac` are original synthetic stereo signals encoded
by the unmodified Fraunhofer FDK AAC 2.0.3 encoder with audio object type 29
and 32 kbit/s output. They contain 27 and 51 complete access units. The core
rate is 24 kHz; PS reconstructs two output channels at 48 kHz. The tonal signal
contains independent channel tones and sweeps. The transient signal combines
chirps, deterministic noise, high-frequency tones and exponential bursts.

The corresponding complete float32 little-endian PCM references come from
unmodified FFmpeg 6.1.1: `ffmpeg -i input.aac -f f32le output.f32le`.
The tests compare all samples with implicit and explicit PS signaling, require
at least 60 dB signal/error ratio for the tonal sequence and 95 dB for the
transient sequence, and check maximum absolute errors of 0.001 and 0.0001.
The observed ratios are 63.70 dB and 100.59 dB respectively. These are
interoperability measurements against an independent decoder.

`parameters.aac` preserves the AAC and SBR payloads of `transients.aac` and
replaces its PS extension with frequency-coded parameter vectors. It cycles
through 10, 20 and 34 parameters, both IID quantizers, both mixing procedures,
variable envelope borders, phase parameters and resolution changes. The IID
indices are `(band / 3 + frame / 3 + envelope) % 7 - 3`; ICC indices are
`(band + frame / 4 + envelope) % 8`. IPD and OPD indices are
`(3 * band + frame + envelope) % 8` and
`(5 * band + frame / 2 + envelope) % 8`. Integer divisions truncate.

The `parameters.f32le.gz` reference is a separate mathematical control, not
unmodified FFmpeg output. It uses the FFmpeg 9.0.2 AAC/SBR decoder with the
following PS-layer changes required by the published matrix equations and
resolution-state rules:

- Conjugate both interpolation endpoints for the starred hybrid subbands;
  the 34-band starred indices are 9–11, and the 20-band indices are 0–1.
- Map the previous unphased real mixing coefficients on a resolution change,
  then reset the phase smoothing angles to zero.
- Clear imaginary target coefficients where phase parameters are absent.
- Interpolate the complex matrices when a phase-bearing frame is followed by
  a frame whose target phase is zero.
- Reset the current frame's phase-enable flag before reading its optional
  extension, so an absent phase extension does not reuse an earlier flag.

No AAC spectral reconstruction, SBR processing or QMF synthesis module was
changed for this mathematical control. The complete comparison achieves
103.88 dB signal/error ratio with maximum absolute error below 0.000029.
Unmodified FFmpeg differs on this deliberately changing phase sequence;
that output is not used as the conformance target. The separate analytic
tests verify hybrid reconstruction, energy conservation, IID ratios, phase
smoothing and the signed alias mappings without a decoder reference.

The mathematical definitions are in ISO/IEC 14496-3:2001/Amd.2:2004,
subclauses 8.6.4.6.2–8.6.4.6.4, and the incomplete-parameter and resolution
state corrections are in Cor.1:2005, subclauses 8.6.4.6.1 and 8.6.4.6.5.
The corresponding complete technical text is also available in the
[3GPP parametric stereo submission SP-040428](https://www.3gpp.org/ftp/tsg_sa/TSG_SA/TSGS_24/Docs/PDF/SP-040428.pdf),
subclauses 8.6.1.6.2–8.6.1.6.4 and Tables 8.22–8.23. The
[published corrigendum](https://cdn.standards.iteh.ai/samples/41704/c1fee3b645ae4ff88b78c63a9959180c/ISO-IEC-14496-3-2001-Amd-2-2004-Cor-1-2005.pdf)
defines the corrected state transitions and incomplete parameter sets.
The reference implementation revisions are
[FDK AAC v2.0.3](https://github.com/mstorsjo/fdk-aac/tree/v2.0.3),
[FFmpeg n6.1.1](https://github.com/FFmpeg/FFmpeg/tree/n6.1.1) and
[FFmpeg n9.0.2 PS processing](https://github.com/FFmpeg/FFmpeg/blob/n9.0.2/libavcodec/aacps.c).

`references.json` records SHA-256 hashes of each encoded input and its
uncompressed PCM. Gzip only reduces fixture storage. The shared verifier
also checks timestamps, exact output lengths, EOF, a constant live state
within a 256 KiB media-buffer budget and complete buffer release. Separate
tests verify reset reproducibility and retained output ownership.

`transients-rap.f32le.gz` is an additional unmodified FFmpeg 6.1.1 reference
for the five access units beginning at packet 10, decoded with a fresh decoder.
This packet contains independent SBR and PS headers. The restart test skips
two warmup packets, compares every returned sample above 95 dB with maximum
error below 0.0001, verifies original timestamps and reproduces output after
out-of-order requests. An explicit PS inspector rejects an otherwise valid
mono HE-AAC v1 header that contains no PS restart information.

`transients-single-rate.f32le.gz` decodes the same 51 access units with explicit
HE-AAC v2 signaling for 24 kHz stereo output using unmodified FFmpeg 6.1.1.
The complete comparison covers 104448 samples, achieves 102.76 dB and has
maximum absolute error below 0.000031. Both forward AOT 29 and backward
sync-extension PS signaling are tested. Synthesis uses the 32-band QMF bank;
it does not resample 48 kHz PCM.

`transients-av.mp4` copies the H.264 video from the HE-AAC HLS fixture and the
unchanged PS access units from `transients.aac`, remuxed with FFmpeg 6.1.1 and
trimmed to two seconds. Its SHA-256 is
`5cf767c17b7d323dd3df509c72672e404561906ab34e2e8927ecc7df6ccbbb1b`.
The first PS frame carries an empty envelope: it is a valid stream origin but
is not an arbitrary independent PS restart. The HLS test requests late, first,
middle and repeated windows, checks bounded source reads and audio clocks,
and requires identical repeated fragments and complete storage release.
