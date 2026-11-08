# HE-AAC references

The QMF fixtures are the complete three-frame response to a unit impulse at core
sample 13, followed by zero samples. `qmf-impulse-bands.f32le.gz` contains 96 slots
of 32 complex subbands, real and imaginary float32 components interleaved.
`qmf-impulse-out.f32le.gz` contains 6144 real output samples. Both use the
PCM16 amplitude domain; the test divides synthesis output by 32768.

The independent decoder is the floating-point reference distributed with 3GPP
TS 26.410, enhanced aacPlus source dated 2009-07. Analysis uses 32 channels;
synthesis uses 64 channels without parametric stereo or low-power processing.
Its source modules are unchanged. The impulse caller clears the synthesis
scratch arrays between frames because the synthesis routine modifies them.
The original C# filterbank follows ISO/IEC 14496-3, section 4.6.18.4, with the
canonical 640-coefficient prototype from Table 4.A.89.

The tests compare every analysis component and synthesis sample, require more
than 130 dB signal-to-error ratio, check the synthesis maximum absolute error,
and verify that all allocated media buffers are released.

The mono and stereo HE-AAC fixtures contain the first 16 complete frames of
original generated signals: independent channel chirps, 7.4/9.5 kHz tones,
deterministic noise and periodic exponential bursts. The floating-point 3GPP
encoder uses an AAC-LC core at 24 kHz, SBR output at 48 kHz, 32 kbit/s mono or
48 kbit/s stereo, without parametric stereo. Each frame produces 2048 output
samples per channel, including the initial filter delay.

FFmpeg 6.1.1 supplies the complete independent float32 PCM references. Stereo
decoding uses `-threads 1 -i input.aac -f f32le output.f32le`; mono uses
`-threads 1 -i input.aac -af 'pan=mono|c0=c0' -f f32le output.f32le` to select
one channel rather than applying the gain of a stereo-to-mono downmix.
`references.json` records source and decoded PCM SHA-256 hashes. Gzip only
reduces fixture storage; the tests verify the decompressed bytes.

The shared managed/Native AOT verifier decodes every sample with implicit and
explicit SBR signaling. It requires at least 80 dB signal-to-error ratio and
maximum absolute error of 0.00025, checks the 48 kHz format, timestamps,
2048-sample durations, EOF, constant live state within a 256 KiB media-buffer
budget, and final release of every media buffer. Separate lifecycle tests cover
reset reproducibility, retained output, cancellation, truncated packets,
missing initial headers and rejection of a non-origin restart without the
required persistent SBR state.
